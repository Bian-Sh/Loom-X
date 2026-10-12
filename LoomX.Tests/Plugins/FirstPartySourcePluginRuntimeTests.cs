using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using LoomX.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// 真实第一方源码包经离线编译器、独立 ALC、宿主 Bridge 和 Pipeline 执行；不实例化静态插件类型。
/// RED 证据：2026-10-12 旧清单运行先得到两项 assembly 断言失败，再得到三项失败（含真实 Runtime 加载）。
/// </summary>
public sealed class FirstPartySourcePluginRuntimeTests
{
    private const string CredentialId = "loomx.credential-protection";
    private const string CompressionId = "loomx.tool-result-compression";
    private const string Secret = "sk-source-runtime-secret-abcdefghijklmnop123456";
    private const string LegacyPlaceholder = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
    private static readonly string[] Cultures = ["zh-CN", "zh-TW", "ja-JP", "en-US"];
    private static readonly IReadOnlyDictionary<string, string> Metadata =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["api_mode"] = "openai" };

    [Theory]
    [InlineData(CredentialId, "LoomX.CredentialProtection")]
    [InlineData(CompressionId, "LoomX.ToolResultCompression")]
    public void 第一方清单必须显式声明源码Runtime(string pluginId, string sourceDirectory)
    {
        // 源项目与测试输出都守住契约，不能只靠静态引用或仓库兜底掩盖输出负载缺失。
        foreach (var package in new[]
        {
            Path.Combine(RepositoryRoot(), "plugins", sourceDirectory),
            Path.Combine(AppContext.BaseDirectory, "plugins", pluginId),
        })
        {
            var manifest = ReadManifest(package);
            Assert.Equal(pluginId, manifest.Id);
            Assert.NotNull(manifest.Runtime);
            Assert.Equal("csharp-source", manifest.Runtime.Kind);
            Assert.False(string.IsNullOrWhiteSpace(manifest.Runtime.EntryType));
            Assert.NotNull(manifest.Runtime.Sources);
            Assert.NotEmpty(manifest.Runtime.Sources);
            Assert.Equal(manifest.Runtime.Sources.Count,
                manifest.Runtime.Sources.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var relativePath in manifest.Runtime.Sources)
                Assert.True(File.Exists(PackageFile(package, relativePath)), "源码负载缺失：" + relativePath);
            foreach (var culture in Cultures)
                Assert.True(File.Exists(Path.Combine(package, "i18n", culture + ".json")));
            if (pluginId == CredentialId)
            {
                Assert.Contains("storage.sqlite/v1", manifest.HostCapabilities);
                Assert.Contains("protection/v1", manifest.HostCapabilities);
            }
        }
    }

    [Fact]
    public async Task 两个真实源码包必须动态编译并注册Pipeline()
    {
        var package = new RuntimePackage(CredentialId, CompressionId);
        await using var runtime = package.Start();
        Assert.True(runtime.PluginInfos.Count == 2, FailureSummary(runtime));
        AssertLoaded(runtime, CredentialId, 2);
        AssertLoaded(runtime, CompressionId, 1);
        var requests = runtime.Pipelines["request"].Entries;
        Assert.Equal(2, requests.Count);
        var credential = Assert.Single(requests, entry => entry.PluginId == CredentialId).Extension;
        var compression = Assert.Single(requests, entry => entry.PluginId == CompressionId).Extension;
        Assert.IsAssignableFrom<IRequestExtension>(credential);
        Assert.IsAssignableFrom<IRequestExtension>(compression);
        Assert.Equal(ExtensionFailurePolicy.FailClosed, credential.FailurePolicy);
        Assert.Equal(ExtensionFailurePolicy.ContinueOnError, compression.FailurePolicy);
        var response = Assert.Single(runtime.Pipelines["response"].Entries).Extension;
        Assert.IsAssignableFrom<IResponseExtension>(response);
        Assert.Equal(ExtensionFailurePolicy.FailClosed, response.FailurePolicy);
        foreach (var extension in new[] { credential, compression, response })
            AssertDynamicAssembly(extension.GetType().Assembly, package.CacheRoot);
    }

    [Fact]
    public async Task 动态凭据请求脱敏并注入完整性指令且响应精确恢复()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        var original = CredentialRequest(Secret);
        var masked = await Request(runtime, original);
        var placeholder = AssertMasked(masked, Secret);
        AssertIntegrityInstruction(masked.Payload);
        Assert.Equal(Secret, UserContent(original));
        Assert.Equal("原有系统指令", JsonNode.Parse(original)!["messages"]![0]!["content"]!.GetValue<string>());

        var restored = await Response(runtime, ResponsePayload("  `" + placeholder + "`  "));
        Assert.Equal(PipelineOutcome.Modified, restored.Outcome);
        Assert.Equal("  `" + Secret + "`  ", ResponseContent(restored.Payload));
        var row = ReadVault(package);
        Assert.Equal(placeholder, row.Token);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))), row.Hash);
        Assert.StartsWith("dpapi:v1:", row.Protected);
        Assert.DoesNotContain(Secret, row.Protected, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(package.DataRoot, "credential-tokens.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task 动态凭据直接恢复测试生成的历史Vault且复用原映射()
    {
        var package = new RuntimePackage(CredentialId);
        SeedLegacyVault(package);
        var historical = ReadVault(package);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        // 同时验证受限大小写归一化及外围空白、反引号不被重写。
        var restored = await Response(runtime, ResponsePayload("  `" + LegacyPlaceholder.ToLowerInvariant() + "`  "));
        Assert.Equal(PipelineOutcome.Modified, restored.Outcome);
        Assert.Equal("  `" + Secret + "`  ", ResponseContent(restored.Payload));
        var masked = await Request(runtime, CredentialRequest(Secret));
        Assert.Equal(LegacyPlaceholder, AssertMasked(masked, Secret));
        Assert.Equal(historical, ReadVault(package));
        Assert.Single(Directory.EnumerateFiles(package.DataRoot, "credential-tokens.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task 动态凭据历史密文损坏时响应安全失败且不透传原文()
    {
        var package = new RuntimePackage(CredentialId);
        SeedLegacyVault(package);
        using (var connection = OpenVault(package, readOnly: false))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE CredentialTokens SET ProtectedOriginal = $ciphertext WHERE Token = $token";
            command.Parameters.AddWithValue("$ciphertext", "dpapi:v1:损坏的测试密文");
            command.Parameters.AddWithValue("$token", LegacyPlaceholder);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        var result = await Response(runtime, ResponsePayload(LegacyPlaceholder));
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        Assert.DoesNotContain(Secret, result.Diagnostic ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("损坏的测试密文", result.Diagnostic ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 动态凭据禁用只停止新脱敏且历史指令与恢复仍有效()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        var placeholder = AssertMasked(await Request(runtime, CredentialRequest(Secret)), Secret);
        Assert.True(runtime.SetPluginEnabled(CredentialId, false));
        Assert.False(Assert.Single(runtime.PluginInfos).Enabled);
        var plain = CredentialRequest("sk-unprotected-after-disable-abcdefghijklmnop");
        var passed = await Request(runtime, plain);
        Assert.Equal(PipelineOutcome.Passed, passed.Outcome);
        Assert.Equal(plain, passed.Payload);
        var historical = await Request(runtime, CredentialRequest(placeholder));
        Assert.Equal(PipelineOutcome.Modified, historical.Outcome);
        Assert.Equal(placeholder, UserContent(historical.Payload));
        AssertIntegrityInstruction(historical.Payload);
        var restored = await Response(runtime, ResponsePayload(placeholder));
        Assert.Equal(PipelineOutcome.Modified, restored.Outcome);
        Assert.Equal(Secret, ResponseContent(restored.Payload));
        Assert.Equal(placeholder, ReadVault(package).Token);
    }

    [Fact]
    public async Task 动态凭据重载退休旧Bridge并保留Vault及禁用语义()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        var liveRequest = runtime.GetPipeline("request")!;
        var placeholder = AssertMasked(await Request(runtime, CredentialRequest(Secret)), Secret);
        var oldAssembly = runtime.Pipelines["request"].Entries[0].Extension.GetType().Assembly;
        var oldBridge = Assert.Single(package.Scopes);
        var historical = ReadVault(package);
        Assert.True(runtime.SetPluginEnabled(CredentialId, false));
        Assert.True(await runtime.ReloadAsync(CredentialId), FailureSummary(runtime));
        AssertLoaded(runtime, CredentialId, 2);
        Assert.False(Assert.Single(runtime.PluginInfos).Enabled);
        Assert.NotSame(oldAssembly, runtime.Pipelines["request"].Entries[0].Extension.GetType().Assembly);
        Assert.Equal(2, package.Scopes.Count);
        var error = await Assert.ThrowsAsync<PluginBridgeException>(async () =>
            await oldBridge.Protection.DecryptAsync(historical.Protected));
        Assert.Equal(PluginBridgeError.Disposed, error.Error);
        var restored = await Response(runtime, ResponsePayload(placeholder));
        Assert.Equal(Secret, ResponseContent(restored.Payload));
        AssertIntegrityInstruction((await Request(runtime, CredentialRequest(placeholder))).Payload);
        var plain = CredentialRequest(Secret);
        Assert.Equal(plain, (await liveRequest.ExecuteAsync(plain)).Payload);
        Assert.True(runtime.SetPluginEnabled(CredentialId, true));
        Assert.Equal(placeholder, AssertMasked(await Request(runtime, plain), Secret));
        Assert.Equal(historical, ReadVault(package));
    }

    [Fact]
    public async Task 动态压缩经Request折叠历史结果但保留最近工具结果()
    {
        var package = new RuntimePackage(CompressionId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CompressionId, 1);
        var original = CompressionRequest();
        var result = await Request(runtime, original);
        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        var before = JsonNode.Parse(original)!;
        var after = JsonNode.Parse(result.Payload)!;
        Assert.Contains("×200", after["messages"]![1]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before["messages"]![3]!["content"]!.GetValue<string>(),
            after["messages"]![3]!["content"]!.GetValue<string>());
        Assert.Equal("call_1", after["messages"]![1]!["tool_call_id"]!.GetValue<string>());
        const string invalid = "不是 JSON 的普通请求";
        var passed = await Request(runtime, invalid);
        Assert.Equal(PipelineOutcome.Passed, passed.Outcome);
        Assert.Equal(invalid, passed.Payload);
    }

    [Fact]
    public async Task 动态压缩UI配置落在插件目录且重载后仍生效()
    {
        var package = new RuntimePackage(CompressionId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CompressionId, 1);
        var command = await runtime.ExecuteUiCommandAsync(CompressionId,
            PluginUiCommand.Create("settings", "toggle_enabled", new Dictionary<string, string> { ["enabled"] = "false" }));
        Assert.True(command.Handled);
        Assert.Equal(PluginUiTone.Success, command.Tone);
        var configPath = Path.Combine(package.DataRoot, CompressionId, "config.json");
        using (var config = JsonDocument.Parse(File.ReadAllText(configPath)))
            Assert.False(config.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Single(Directory.EnumerateFiles(package.DataRoot, "config.json", SearchOption.AllDirectories));
        var oldAssembly = runtime.Pipelines["request"].Entries[0].Extension.GetType().Assembly;
        Assert.True(await runtime.ReloadAsync(CompressionId), FailureSummary(runtime));
        Assert.NotSame(oldAssembly, runtime.Pipelines["request"].Entries[0].Extension.GetType().Assembly);
        var payload = CompressionRequest();
        var disabled = await Request(runtime, payload);
        Assert.Equal(PipelineOutcome.Passed, disabled.Outcome);
        Assert.Equal(payload, disabled.Payload);
        var enabled = await runtime.ExecuteUiCommandAsync(CompressionId,
            PluginUiCommand.Create("settings", "toggle_enabled", new Dictionary<string, string> { ["enabled"] = "true" }));
        Assert.True(enabled.Handled);
        Assert.Equal(PluginUiTone.Success, enabled.Tone);
        Assert.Equal(PipelineOutcome.Modified, (await Request(runtime, payload)).Outcome);
    }

    [Fact]
    public async Task 动态凭据UI新增前缀落库并跨重载参与实际请求脱敏()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        const string prefix = "sourceonly-";
        const string customSecret = prefix + "abcdefghijklmnopqrstuv123456";
        var payload = CredentialRequest(customSecret);
        Assert.Equal(PipelineOutcome.Passed, (await Request(runtime, payload)).Outcome);
        var command = await runtime.ExecuteUiCommandAsync(CredentialId,
            PluginUiCommand.Create("settings", "add_prefix", new Dictionary<string, string> { ["new_prefix"] = prefix }));
        Assert.True(command.Handled);
        Assert.Equal(PluginUiTone.Success, command.Tone);
        using (var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(package.DataRoot, CredentialId, "rules.json"))))
            Assert.Contains(rules.RootElement.GetProperty("secret_prefixes").EnumerateArray(), item => item.GetString() == prefix);
        var placeholder = AssertMasked(await Request(runtime, payload), customSecret);
        Assert.True(await runtime.ReloadAsync(CredentialId), FailureSummary(runtime));
        Assert.Equal(placeholder, AssertMasked(await Request(runtime, payload), customSecret));
        Assert.Equal(customSecret, ResponseContent((await Response(runtime, ResponsePayload(placeholder))).Payload));
    }

    [Theory]
    [InlineData(CredentialId)]
    [InlineData(CompressionId)]
    public async Task 动态插件四语言展示与设置节点来自对应插件负载(string pluginId)
    {
        var package = new RuntimePackage(pluginId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, pluginId, pluginId == CredentialId ? 2 : 1);
        foreach (var culture in Cultures)
        {
            using var text = JsonDocument.Parse(File.ReadAllText(Path.Combine(package.PluginRoot, pluginId, "i18n", culture + ".json")));
            var display = runtime.GetPluginDisplayInfo(pluginId, culture);
            Assert.NotNull(display);
            Assert.Equal(text.RootElement.GetProperty("plugin.name").GetString(), display.DisplayName);
            Assert.Equal(text.RootElement.GetProperty("plugin.description").GetString(), display.Description);
            var card = Assert.Single(runtime.GetUiContributions(pluginId, PluginUiSlot.CardBody, culture));
            var settings = Assert.Single(runtime.GetUiContributions(pluginId, PluginUiSlot.DetailBody, culture));
            Assert.Equal("observability", card.Id);
            Assert.Equal("settings", settings.Id);
            Assert.Equal(PluginUiContribution.CurrentSchemaVersion, settings.SchemaVersion);
            var key = pluginId == CredentialId ? "addSectionTitle" : "settings.savings_title";
            Assert.Contains(Nodes(settings.Root).OfType<PluginUiTextNode>(),
                node => node.Text == text.RootElement.GetProperty(key).GetString());
            Assert.Contains(Nodes(settings.Root), node => node is PluginUiButtonNode or PluginUiToggleFieldNode or PluginUiChoiceFieldNode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 动态凭据通过生产流包装器恢复跨网络块与跨SSE事件(bool toolArguments)
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        AssertLoaded(runtime, CredentialId, 2);
        var placeholder = AssertMasked(await Request(runtime, CredentialRequest(Secret)), Secret);
        var first = placeholder[..19];
        var second = placeholder[19..];
        string Frame(string text) => "data: " + (toolArguments
            ? new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject
                { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["function"] = new JsonObject { ["arguments"] = text } }) } }) }
            : new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = text } }) }).ToJsonString() + "\n\n";
        var body = Frame(first) + Frame(second) + "data: [DONE]\n\n";
        await using var restoring = new SsePipelineRestoringStream(new TinyChunkStream(Encoding.UTF8.GetBytes(body)),
            runtime.GetPipeline("response")!, new ProviderExecutionContext("test", "test", "openai", "/test"), null, CancellationToken.None);
        using var reader = new StreamReader(restoring, Encoding.UTF8);
        var restored = await reader.ReadToEndAsync();
        Assert.Contains(Secret, restored);
        Assert.DoesNotContain(placeholder, restored);
        Assert.DoesNotContain("LOOMX_CREDENTIAL_", restored);
        Assert.EndsWith("data: [DONE]\n\n", restored);
        foreach (var frame in restored.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).SkipLast(1))
            using (JsonDocument.Parse(frame["data: ".Length..])) { }
    }

    [Fact]
    public async Task 动态凭据通过生产流包装器恢复跨网络块普通响应()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        var placeholder = AssertMasked(await Request(runtime, CredentialRequest(Secret)), Secret);
        await using var restoring = new PipelineRestoringStream(new TinyChunkStream(Encoding.UTF8.GetBytes(ResponsePayload(placeholder))),
            runtime.GetPipeline("response")!, new ProviderExecutionContext("test", "test", "openai", "/test"), null, CancellationToken.None);
        using var reader = new StreamReader(restoring, Encoding.UTF8);
        var restored = await reader.ReadToEndAsync();
        Assert.Equal(Secret, ResponseContent(restored));
        Assert.DoesNotContain(placeholder, restored);
    }

    [Fact]
    public async Task 动态凭据通过生产SSE包装器在残缺引用结束时安全失败()
    {
        var package = new RuntimePackage(CredentialId);
        await using var runtime = package.Start();
        var placeholder = AssertMasked(await Request(runtime, CredentialRequest(Secret)), Secret);
        var frame = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0,
            ["delta"] = new JsonObject { ["content"] = placeholder[..20] } }) };
        var body = "data: " + frame.ToJsonString() + "\n\ndata: [DONE]\n\n";
        await using var restoring = new SsePipelineRestoringStream(new TinyChunkStream(Encoding.UTF8.GetBytes(body)),
            runtime.GetPipeline("response")!, new ProviderExecutionContext("test", "test", "openai", "/test"), null, CancellationToken.None);
        using var reader = new StreamReader(restoring, Encoding.UTF8);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadToEndAsync());
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain(placeholder, error.ToString());
    }

    // 强制源流以五字节块读出，不让测试只验证单块完整响应。
    private sealed class TinyChunkStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 5)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(count, 5), cancellationToken);
    }
    private static string RepositoryRoot([CallerFilePath] string testSourceFile = "")
    {
        // VSTest 可把工作目录切到输出位置；独立构建时回溯本测试的编译源路径，不猜测主工作目录。
        foreach (var candidate in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(testSourceFile)! })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx")))
                directory = directory.Parent;
            if (directory is not null) return directory.FullName;
        }
        throw new InvalidOperationException("无法定位本测试源码所属工作树。");
    }
    private static PluginManifest ReadManifest(string directory)
    {
        var errors = new List<string>();
        var manifest = ManifestParser.Parse(File.ReadAllText(Path.Combine(directory, ManifestParser.ManifestFileName)), errors);
        Assert.Empty(errors);
        Assert.NotNull(manifest);
        return manifest;
    }

    private static string PackageFile(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase));
        return full;
    }

    private static void AssertLoaded(PluginRuntime runtime, string pluginId, int extensionCount)
    {
        Assert.True(runtime.PluginInfos.Any(info => info.Id == pluginId), FailureSummary(runtime));
        var info = Assert.Single(runtime.PluginInfos, item => item.Id == pluginId);
        Assert.Equal("csharp-source", info.RuntimeKind);
        Assert.Equal(extensionCount, info.ExtensionCount);
        Assert.True(info.HasCardUi);
        Assert.True(info.HasDetailUi);
        Assert.Contains("settings", info.InteractiveUiContributions ?? []);
    }

    private static string FailureSummary(PluginRuntime runtime) =>
        "实际加载数=" + runtime.PluginInfos.Count + "；" + string.Join("；", runtime.StructuredDiagnostics.Select(
            diagnostic => $"{diagnostic.PluginId}/{diagnostic.Stage}/{diagnostic.Code}/{diagnostic.RelativePath}:{diagnostic.Line}"));

    private static void AssertDynamicAssembly(Assembly assembly, string cacheRoot)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        Assert.NotNull(context);
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.True(context.IsCollectible);
        Assert.StartsWith("LoomX.Source.", assembly.GetName().Name!);
        Assert.StartsWith(Path.GetFullPath(cacheRoot) + Path.DirectorySeparatorChar, assembly.Location, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PipelineResult> Request(PluginRuntime runtime, string payload)
    {
        var pipeline = Assert.IsAssignableFrom<IContextualPipeline>(runtime.GetPipeline("request"));
        return await pipeline.ExecuteAsync(payload, Metadata);
    }

    private static async Task<PipelineResult> Response(PluginRuntime runtime, string payload)
    {
        var pipeline = runtime.GetPipeline("response");
        Assert.NotNull(pipeline);
        return await pipeline.ExecuteAsync(payload);
    }

    private static string CredentialRequest(string value) => new JsonObject
    {
        ["model"] = "test-model",
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = "原有系统指令" },
            new JsonObject { ["role"] = "user", ["content"] = value }),
    }.ToJsonString();

    private static string UserContent(string payload) => JsonNode.Parse(payload)!["messages"]!.AsArray()
        .Single(message => message!["role"]!.GetValue<string>() == "user")!["content"]!.GetValue<string>();

    private static string AssertMasked(PipelineResult result, string secret)
    {
        Assert.Equal(PipelineOutcome.Modified, result.Outcome);
        Assert.DoesNotContain(secret, result.Payload, StringComparison.Ordinal);
        var placeholder = UserContent(result.Payload);
        Assert.Matches(@"^\{\{LOOMX_CREDENTIAL_[A-Z2-7]{20}\}\}$", placeholder);
        return placeholder;
    }

    private static void AssertIntegrityInstruction(string payload) => Assert.Contains(
        JsonNode.Parse(payload)!["messages"]!.AsArray(), message =>
            message?["role"]?.GetValue<string>() is "system" or "developer"
            && message["content"]!.GetValue<string>().Contains("LOOMX_CREDENTIAL_", StringComparison.Ordinal));

    private static string ResponsePayload(string value) => new JsonObject { ["content"] = value }.ToJsonString();
    private static string ResponseContent(string payload) => JsonNode.Parse(payload)!["content"]!.GetValue<string>();

    private static string CompressionRequest() => new JsonObject
    {
        ["model"] = "test-model",
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "user", ["content"] = "普通任务" },
            ToolMessage("call_1", "alpha"), ToolMessage("call_2", "beta"), ToolMessage("call_3", "gamma")),
    }.ToJsonString();

    private static JsonObject ToolMessage(string callId, string tag) => new()
    {
        ["role"] = "tool", ["tool_call_id"] = callId,
        ["content"] = string.Join('\n', Enumerable.Repeat($"INFO  {tag} heartbeat ok", 200)),
    };

    private static void SeedLegacyVault(RuntimePackage package)
    {
        // 仅调用测试保存的旧 SQL/DPAPI 实现，路径严格属于本次测试，绝不读取用户真实 Vault。
        var legacy = new CredentialLegacyVault(Path.Combine(package.DataRoot, CredentialId));
        Assert.Equal(LegacyPlaceholder, legacy.GetOrCreateToken(Secret, () => LegacyPlaceholder));
    }

    private static SqliteConnection OpenVault(RuntimePackage package, bool readOnly = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(package.DataRoot, CredentialId, "credential-tokens.db"),
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static (string Token, string Hash, string Protected) ReadVault(RuntimePackage package)
    {
        using var connection = OpenVault(package);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Token, OriginalHash, ProtectedOriginal FROM CredentialTokens";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var row = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        Assert.False(reader.Read());
        return row;
    }

    private static IEnumerable<PluginUiNode> Nodes(PluginUiNode root)
    {
        yield return root;
        var children = root switch
        {
            PluginUiStackNode stack => stack.Children,
            PluginUiGridNode grid => grid.Children.Select(item => item.Content),
            PluginUiSurfaceNode surface => new[] { surface.Content },
            _ => Enumerable.Empty<PluginUiNode>(),
        };
        foreach (var child in children)
            foreach (var descendant in Nodes(child))
                yield return descendant;
    }

    private sealed class RuntimePackage
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "loomx-first-party-source-runtime", Guid.NewGuid().ToString("N"));
        public string PluginRoot => Path.Combine(root, "plugins");
        public string DataRoot => Path.Combine(root, "data");
        public string CacheRoot => Path.Combine(root, "cache");
        public List<PluginBridgeScope> Scopes { get; } = [];

        public RuntimePackage(params string[] pluginIds)
        {
            foreach (var id in pluginIds)
            {
                var output = Path.Combine(AppContext.BaseDirectory, "plugins", id);
                // 动态验收必须消费实际构建负载；不回落仓库源码掩盖遗漏文件。
                var source = output;
                var manifest = ReadManifest(source);
                var staged = Path.Combine(PluginRoot, id);
                Directory.CreateDirectory(Path.Combine(staged, "i18n"));
                File.Copy(Path.Combine(source, ManifestParser.ManifestFileName), Path.Combine(staged, ManifestParser.ManifestFileName));
                foreach (var relative in manifest.Runtime?.Sources ?? [])
                {
                    var destination = PackageFile(staged, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(PackageFile(source, relative), destination);
                }
                foreach (var culture in Cultures)
                    File.Copy(Path.Combine(source, "i18n", culture + ".json"), Path.Combine(staged, "i18n", culture + ".json"));
            }
        }

        public PluginRuntime Start() => PluginRuntime.Start(new PluginRuntimeOptions
        {
            PluginDirectory = PluginRoot,
            DataRootDirectory = DataRoot,
            SourceCompiler = new SourcePluginCompiler(CacheRoot),
            BridgeFactory = (manifest, _) =>
            {
                var scope = new PluginBridgeScope(manifest.Id, manifest.HostCapabilities,
                    storage: new PluginSqliteStorageBridge(manifest.Id, DataRoot),
                    protection: new PluginProtectionBridge(manifest.Id, NullLogger<PluginProtectionBridge>.Instance));
                Scopes.Add(scope);
                return scope;
            },
        }, NullLoggerFactory.Instance);
        // 临时目录和编译证据保留；await using Runtime 释放句柄，不清理任何既存或其他会话产物。
    }
}
