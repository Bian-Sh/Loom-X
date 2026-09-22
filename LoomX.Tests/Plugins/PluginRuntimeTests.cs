using LoomX.CredentialProtection;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Plugins;

/// <summary>
/// Runtime 端到端：目录发现 → Manifest 验证 → ALC 加载 → 注册 → Pipeline 执行
/// （spec: plugin-runtime-pipeline / 动态加载与契约隔离、启用禁用）。
/// </summary>
public sealed class PluginRuntimeTests : IDisposable
{
    private readonly string rootDirectory =
        Path.Combine(Path.GetTempPath(), "loomx-plugin-runtime-" + Guid.NewGuid().ToString("N"));

    private string PluginDirectory => Path.Combine(rootDirectory, "plugins");
    private string DataDirectory => Path.Combine(rootDirectory, "data");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 插件程序集仍由测试进程的 ALC 持有（collectible ALC 待 GC），临时目录留给系统清理。
        }
    }

    /// <summary>把第一方插件负载（dll + manifest + deps）拷到测试插件目录，模拟生产布局。</summary>
    private string StageCredentialProtectionPlugin()
    {
        var directory = Path.Combine(PluginDirectory, "loomx.credential-protection");
        Directory.CreateDirectory(directory);
        foreach (var fileName in new[]
        {
            "LoomX.CredentialProtection.dll",
            "LoomX.CredentialProtection.deps.json",
            "plugin.manifest.json",
        })
        {
            var source = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(directory, fileName), overwrite: true);
        }

        return directory;
    }

    private PluginRuntime StartRuntime(IDictionary<string, IReadOnlyList<string>>? pipelineOrder = null) =>
        PluginRuntime.Start(
            new PluginRuntimeOptions
            {
                PluginDirectory = PluginDirectory,
                DataRootDirectory = DataDirectory,
                PipelineOrder = pipelineOrder ?? new Dictionary<string, IReadOnlyList<string>>(),
            },
            NullLoggerFactory.Instance);

    [Fact]
    public void CredentialProtectionPlugin_LoadsInIsolatedAlc_WithSharedContractIdentity()
    {
        StageCredentialProtectionPlugin();

        var runtime = StartRuntime();

        var info = Assert.Single(runtime.PluginInfos);
        Assert.Equal("loomx.credential-protection", info.Id);
        Assert.Equal(4, info.ExtensionCount);
        Assert.Empty(runtime.Diagnostics);
        Assert.NotNull(runtime.GetPipeline("request"));
        Assert.NotNull(runtime.GetPipeline("response"));
        Assert.NotNull(runtime.GetPipeline("tool-result"));
        Assert.NotNull(runtime.GetPipeline("persistence"));

        // 契约隔离：插件程序集加载在独立 ALC（与静态引用副本不同），
        // 但契约类型身份共享——extension 可直接 cast 到宿主侧契约接口。
        var requestExtension = runtime.Pipelines["request"].Entries[0].Extension;
        var responseExtension = runtime.Pipelines["response"].Entries[0].Extension;
        var toolResultExtension = runtime.Pipelines["tool-result"].Entries[0].Extension;
        var persistenceExtension = runtime.Pipelines["persistence"].Entries[0].Extension;
        Assert.NotSame(typeof(CredentialProtectionPlugin).Assembly, requestExtension.GetType().Assembly);
        Assert.IsAssignableFrom<IRequestExtension>(requestExtension);
        Assert.IsAssignableFrom<IResponseExtension>(responseExtension);
        Assert.IsAssignableFrom<IToolResultExtension>(toolResultExtension);
        Assert.IsAssignableFrom<IPersistenceExtension>(persistenceExtension);
    }

    [Fact]
    public void InvalidManifestPlugin_IsRejected_WithoutAffectingValidPlugins()
    {
        StageCredentialProtectionPlugin();
        var badDirectory = Path.Combine(PluginDirectory, "bad-plugin");
        Directory.CreateDirectory(badDirectory);
        File.WriteAllText(Path.Combine(badDirectory, "plugin.manifest.json"),
            """{ "version": "1.0.0" }""");

        var runtime = StartRuntime();

        Assert.Single(runtime.PluginInfos);
        Assert.Contains(runtime.Diagnostics, item => item.Contains("id"));
    }

    [Fact]
    public async Task PluginDisable_PipelineSkipsEntries_ReenableRestores()
    {
        StageCredentialProtectionPlugin();
        var runtime = StartRuntime();
        var pipeline = runtime.GetPipeline("request")!;
        const string payload = """{"api_key":"sk-abcdefghij0123456789abcd"}""";

        runtime.SetPluginEnabled("loomx.credential-protection", false);
        var disabledResult = await pipeline.ExecuteAsync(payload);
        Assert.Equal(PipelineOutcome.Passed, disabledResult.Outcome);
        Assert.Equal(payload, disabledResult.Payload);

        runtime.SetPluginEnabled("loomx.credential-protection", true);
        var enabledResult = await pipeline.ExecuteAsync(payload);
        Assert.Equal(PipelineOutcome.Modified, enabledResult.Outcome);
        Assert.DoesNotContain("sk-abcdefghij0123456789abcd", enabledResult.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntryDisable_SkipsOnlyThatEntry()
    {
        StageCredentialProtectionPlugin();
        var runtime = StartRuntime();
        var pipeline = runtime.GetPipeline("request")!;
        const string payload = """{"api_key":"sk-abcdefghij0123456789abcd"}""";

        runtime.SetEntryEnabled("loomx.credential-protection", "credential.request", false);
        var result = await pipeline.ExecuteAsync(payload);
        Assert.Equal(PipelineOutcome.Passed, result.Outcome);

        runtime.SetEntryEnabled("loomx.credential-protection", "credential.request", true);
        var reenabled = await pipeline.ExecuteAsync(payload);
        Assert.Equal(PipelineOutcome.Modified, reenabled.Outcome);
    }
}
