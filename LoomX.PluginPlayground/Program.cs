using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoomX.Plugin.Storage.Sqlite;
using LoomX.Plugins;
using LoomX.Plugins.Host;
using Microsoft.Extensions.Logging;

var failures = new List<string>();
var checks = 0;
void Check(string name, bool passed)
{
    checks++;
    Console.Out.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}");
    if (!passed) failures.Add(name);
}
async Task Scenario(string name, Func<Task> action)
{
    try { await action(); }
    catch (Exception exception)
    {
        // CLI 只输出类型，禁止输出可能含凭据/SQL/源码的任意异常消息。
        Check($"{name}（{exception.GetType().Name}）", false);
    }
}
async Task<bool> BridgeDisposed(PluginBridgeScope scope)
{
    try { await using var unexpected = await scope.Storage.OpenSqliteAsync("retired.db"); return false; }
    catch (PluginBridgeException exception) { return exception.Error == PluginBridgeError.Disposed; }
}
async Task<bool> DatabaseDisposed(IPluginSqliteDatabase database)
{
    try { await database.QueryAsync(new("SELECT 1;")); return false; }
    catch (PluginSqlException exception) { return exception.Error == PluginSqlError.Disposed; }
}
static bool Modified(PipelineResult result, string expected) => result.Outcome == PipelineOutcome.Modified && result.Payload == expected;
static string RequestBody(string value) => JsonSerializer.Serialize(new
{
    model = "playground-fake-model",
    messages = new[] { new { role = "tool", content = "credential=" + value } },
});
static string ResponseBody(string value) => JsonSerializer.Serialize(new { value });
static bool IntegrityPresent(string payload)
{
    using var document = JsonDocument.Parse(payload);
    return document.RootElement.GetProperty("messages").EnumerateArray().Any(message =>
        message.GetProperty("role").GetString() is "system" or "developer"
        && message.GetProperty("content").GetString()?.Contains("LOOMX_CREDENTIAL_", StringComparison.Ordinal) == true);
}
static IReadOnlyDictionary<string, string> CacheFingerprint(string directory) =>
    Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .ToDictionary(file => Path.GetRelativePath(directory, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.Ordinal);
static bool CacheUnchanged(string directory, IReadOnlyDictionary<string, string> expected)
{
    try { return expected.OrderBy(item => item.Key, StringComparer.Ordinal).SequenceEqual(CacheFingerprint(directory).OrderBy(item => item.Key, StringComparer.Ordinal)); }
    catch (IOException) { return false; }
}
static void CopyPayload(string source, string destination)
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: false);
    }
}

// 每次运行新建明确隔离根，保留供交接检查；不访问生产配置/Vault，不清理任何旧产物。
var temporaryRoot = Path.Combine(Path.GetTempPath(), "LoomX-Playground-task7.1-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
Console.Out.WriteLine($"本次隔离临时目录：{temporaryRoot}");
var pluginsRoot = Path.Combine(temporaryRoot, "packages");
var dataRoot = Path.Combine(temporaryRoot, "data");
var cacheRoot = Path.Combine(temporaryRoot, "cache");
Directory.CreateDirectory(pluginsRoot);
Directory.CreateDirectory(dataRoot);
var capture = new CaptureLoggerProvider();
using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(capture));
var bridgeFactory = new PlaygroundBridgeFactory(dataRoot, loggerFactory);
var compiler = new SourcePluginCompiler(cacheRoot, loggerFactory.CreateLogger<SourcePluginCompiler>());
string? issuedToken = null;
const string secret = "sk-playground-example-1234567890";

await Scenario("源码与 Credential 端到端", async () =>
{
    var shippedPayload = Path.Combine(AppContext.BaseDirectory, "plugins", LegacyVaultFixture.PluginId);
    CopyPayload(shippedPayload, Path.Combine(pluginsRoot, LegacyVaultFixture.PluginId));
    Check("Credential 插件包不含宿主 SQLite 动态依赖", !Directory.EnumerateFiles(shippedPayload, "*", SearchOption.AllDirectories)
        .Select(Path.GetFileName).Any(name => name is not null && (name.Equals("Microsoft.Data.Sqlite.dll", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("SQLitePCLRaw.", StringComparison.OrdinalIgnoreCase) || name.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase))));
    Check("插件负载只有 plugins/<id> 布局，无根目录平铺副本",
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "plugin.manifest.json")) && !Directory.Exists(Path.Combine(AppContext.BaseDirectory, "i18n")));
    var sourceDirectory = SourcePluginFixture.Write(pluginsRoot);
    var protectedLegacy = LegacyVaultFixture.Seed(dataRoot);
    var validation = await PluginPackageValidation.ValidateAsync(sourceDirectory);
    Check("公共包校验通过且源码/三种 Bridge 能力一致", validation.Success && validation.Manifest?.Id == SourcePluginFixture.Id
        && validation.Manifest.HostCapabilities.SequenceEqual(new[] { "storage.sqlite/v1", "logging/v1", "notifications/v1" }));
    Check("源码包无需 csproj 或编译程序集", !Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories).Any()
        && !Directory.EnumerateFiles(sourceDirectory, "*.dll", SearchOption.AllDirectories).Any());
    var compilation = await compiler.CompileAsync(sourceDirectory, validation.Manifest!);
    Check("随附源码编译器成功生成程序集及 portable PDB，忽略未声明源码", compilation.Success
        && !compilation.CacheHit && File.Exists(compilation.AssemblyPath) && File.Exists(compilation.PdbPath));
    var cached = await compiler.CompileAsync(sourceDirectory, validation.Manifest!);
    Check("相同输入第二次编译命中相同缓存", cached.Success && cached.CacheHit && cached.CacheKey == compilation.CacheKey);
    PluginRuntimeOptions Options() => new()
    {
        PluginDirectory = pluginsRoot,
        DataRootDirectory = dataRoot,
        SourceCompiler = compiler,
        BridgeFactory = bridgeFactory.Create,
    };
    await using var runtime = PluginRuntime.Start(Options(), loggerFactory);
    Check("Credential Protection 预编译插件已发现", runtime.PluginInfos.Any(info => info.Id == LegacyVaultFixture.PluginId && info.RuntimeKind == "assembly"));
    Check("源码插件经共享契约动态加载", runtime.PluginInfos.Any(info => info.Id == SourcePluginFixture.Id && info.RuntimeKind == "csharp-source"));
    Check("初始 Manifest/编译/能力/ALC 加载无诊断", runtime.Diagnostics.Count == 0 && runtime.StructuredDiagnostics.Count == 0);
    var request = runtime.GetPipeline("request");
    var response = runtime.GetPipeline("response");
    Check("Router Request 与 Response Pipeline 已注册", request is not null && response is not null);
    Check("Credential Request metadata 已注册", runtime.Pipelines.TryGetValue("request", out var requestMetadata)
        && requestMetadata.Entries.Any(entry => entry.PluginId == LegacyVaultFixture.PluginId && entry.Extension.ExtensionId == "credential.request"
            && entry.Extension.Kind == ExtensionKind.Request));
    Check("Credential Response metadata 已注册", runtime.Pipelines.TryGetValue("response", out var responseMetadata)
        && responseMetadata.Entries.Any(entry => entry.PluginId == LegacyVaultFixture.PluginId && entry.Extension.ExtensionId == "credential.response"
            && entry.Extension.Kind == ExtensionKind.Response));
    var sourceRequest = runtime.GetPipeline(SourcePluginFixture.Request);
    var sourceResponse = runtime.GetPipeline(SourcePluginFixture.Response);
    Check("源码成对 Pipeline metadata 与公共执行接口就绪", sourceRequest is not null && sourceResponse is not null
        && runtime.Pipelines.TryGetValue(SourcePluginFixture.Request, out var sourceMetadata)
        && sourceMetadata.Entries.Any(entry => entry.PluginId == SourcePluginFixture.Id && entry.Extension.ExtensionId == "source.request"));

    await Scenario("源码 Bridge 与 reload", async () =>
    {
        if (sourceRequest is null || sourceResponse is null) throw new InvalidOperationException();
        foreach (var operation in new[] { "crud", "commit", "rollback", "dispose-rollback", "constraint-rollback", "transaction-isolation", "path-rejection", "feedback-rejection", "feedback-inactive-union", "notify" })
        {
            var result = await sourceRequest.ExecuteAsync(operation);
            Check($"源码 Bridge {operation} 的真实执行结果", Modified(result, operation + "-ok:g1"));
        }
        Check("源码数据库只位于本插件隔离子目录", File.Exists(Path.Combine(dataRoot, SourcePluginFixture.Id, "cache", "state.db"))
            && !File.Exists(Path.Combine(dataRoot, "other-plugin", "escape.db")));
        Check("Logging Bridge 捕获绑定来源、模板和结构化字段", bridgeFactory.Logs.Any(item => item.Id == SourcePluginFixture.Id
            && item.Entry.Level == PluginLogLevel.Information && item.Entry.MessageTemplate == "源码插件检查完成 {StatusCode}"
            && item.Entry.Fields?.Any(field => field.Name == "StatusCode" && field.Value.Integer == 200) == true));
        Check("Notifications Bridge 捕获绑定来源与 Success 安全摘要", bridgeFactory.Notifications.Any(item => item.Id == SourcePluginFixture.Id
            && item.Notification.Level == PluginNotificationLevel.Success && item.Notification.Message == "源码插件检查完成"));
        Check("源码成对 request/response 使用 g1", Modified(await sourceResponse.ExecuteAsync((await sourceRequest.ExecuteAsync("消息")).Payload), "消息|request:g1|response:g1"));
        runtime.SetEntryEnabled(SourcePluginFixture.Id, "source.request", false);
        var disabled = await sourceRequest.ExecuteAsync("消息");
        Check("源码 Entry 禁用后保持原样", disabled.Outcome == PipelineOutcome.Passed && disabled.Payload == "消息");
        runtime.SetEntryEnabled(SourcePluginFixture.Id, "source.request", true);
        Check("源码 Entry 重新启用恢复执行", Modified(await sourceRequest.ExecuteAsync("消息"), "消息|request:g1"));
        runtime.SetPluginEnabled(SourcePluginFixture.Id, false);
        Check("源码普通插件禁用跳过执行", (await sourceRequest.ExecuteAsync("消息")).Outcome == PipelineOutcome.Passed);
        runtime.SetPluginEnabled(SourcePluginFixture.Id, true);
        Check("源码普通插件启用恢复执行", Modified(await sourceRequest.ExecuteAsync("storage"), "storage-ok:g1"));
        var oldScope = bridgeFactory.Scopes.Single(item => item.Id == SourcePluginFixture.Id).Scope;
        await using var oldHandle = await oldScope.Storage.OpenSqliteAsync("cache/state.db");
        await using var lease = runtime.AcquireLease();
        var oldRequest = lease.GetPipeline(SourcePluginFixture.Request)!;
        var oldResponse = lease.GetPipeline(SourcePluginFixture.Response)!;
        SourcePluginFixture.Write(pluginsRoot, "g2");
        Check("成功 reload 返回成功", await runtime.ReloadAsync(SourcePluginFixture.Id));
        Check("新注册 metadata 与版本切换到 g2", runtime.PluginInfos.Single(info => info.Id == SourcePluginFixture.Id).Version == "2.0.0");
        Check("reload 前保存的动态执行接口取得新代际", Modified(await sourceRequest.ExecuteAsync("storage"), "storage-ok:g2"));
        Check("新调用 request/response 使用同一 g2", Modified(await sourceResponse.ExecuteAsync((await sourceRequest.ExecuteAsync("消息")).Payload), "消息|request:g2|response:g2"));
        Check("旧租约仍能使用 g1 SQLite 与成对 Pipeline", Modified(await oldRequest.ExecuteAsync("storage"), "storage-ok:g1")
            && Modified(await oldResponse.ExecuteAsync((await oldRequest.ExecuteAsync("消息")).Payload), "消息|request:g1|response:g1"));
        Check("旧租约未释放时旧 Bridge 仍可用", (await oldHandle.QuerySingleAsync(new("SELECT 1;")))?[0].Integer == 1);
        await lease.DisposeAsync();
        Check("旧租约排空后 Bridge 确定退休", await BridgeDisposed(oldScope));
        Check("旧租约排空后数据库句柄拒绝复用", await DatabaseDisposed(oldHandle));
        Check("退休拒绝调用不创建新数据库", !File.Exists(Path.Combine(dataRoot, SourcePluginFixture.Id, "retired.db")));
        var currentScope = bridgeFactory.Scopes.Last(item => item.Id == SourcePluginFixture.Id).Scope;
        await using var currentHandle = await currentScope.Storage.OpenSqliteAsync("cache/state.db");
        var scopesBeforeFailure = bridgeFactory.Scopes.Count;
        var g2Validation = await PluginPackageValidation.ValidateAsync(sourceDirectory);
        var g2Compilation = await compiler.CompileAsync(sourceDirectory, g2Validation.Manifest!);
        Check("成功 reload 后最近可用 g2 编译缓存身份可追踪", g2Compilation.Success && g2Compilation.CacheHit
            && g2Compilation.CacheKey != compilation.CacheKey);
        var g2Cache = Path.Combine(cacheRoot, SourcePluginFixture.Id, g2Compilation.CacheKey!);
        var g2Fingerprint = CacheFingerprint(g2Cache);
        Check("g2 缓存摘要包含程序集、PDB 与完整性元数据", g2Fingerprint.ContainsKey("cache.json") && g2Fingerprint.ContainsKey("cache.sig")
            && g2Fingerprint.ContainsKey(Path.GetRelativePath(g2Cache, g2Compilation.AssemblyPath!))
            && g2Fingerprint.ContainsKey(Path.GetRelativePath(g2Cache, g2Compilation.PdbPath!)));
        var intactRequest = runtime.Pipelines[SourcePluginFixture.Request].Entries.Single().Extension;
        var intactResponse = runtime.Pipelines[SourcePluginFixture.Response].Entries.Single().Extension;
        async Task CheckRetainedPair(string stage)
        {
            var metadataSnapshot = runtime.Pipelines;
            Check($"{stage}保留成对注册的原 Extension 身份", metadataSnapshot.TryGetValue(SourcePluginFixture.Request, out var registeredRequest)
                && metadataSnapshot.TryGetValue(SourcePluginFixture.Response, out var registeredResponse)
                && registeredRequest.Entries.Count == 1 && registeredResponse.Entries.Count == 1
                && ReferenceEquals(registeredRequest.Entries[0].Extension, intactRequest)
                && ReferenceEquals(registeredResponse.Entries[0].Extension, intactResponse));
            await using var retainedLease = runtime.AcquireLease();
            var retainedRequest = retainedLease.GetPipeline(SourcePluginFixture.Request);
            var retainedResponse = retainedLease.GetPipeline(SourcePluginFixture.Response);
            Check($"{stage}后同租约成对 Pipeline 仍执行原 g2", retainedRequest is not null && retainedResponse is not null
                && Modified(await retainedResponse.ExecuteAsync((await retainedRequest.ExecuteAsync("消息")).Payload), "消息|request:g2|response:g2"));
        }
        File.WriteAllText(Path.Combine(sourceDirectory, "src", "Plugin.cs"), "#error " + SourcePluginFixture.Sentinel);
        Check("源码编译失败 reload 返回失败", !await runtime.ReloadAsync(SourcePluginFixture.Id));
        Check("编译失败保留 g2 实例与 Bridge", runtime.PluginInfos.Single(info => info.Id == SourcePluginFixture.Id).Version == "2.0.0"
            && bridgeFactory.Scopes.Count == scopesBeforeFailure && Modified(await sourceRequest.ExecuteAsync("storage"), "storage-ok:g2"));
        Check("编译失败提供安全相对路径与行列诊断", runtime.StructuredDiagnostics.Any(item => item.PluginId == SourcePluginFixture.Id
            && item.Stage == PluginDiagnosticStage.Compile && item.Code == "CS1029" && item.RelativePath == "src/Plugin.cs" && item.Line > 0 && item.Column > 0));
        Check("失败编译不破坏已发布 g1 缓存", File.Exists(compilation.AssemblyPath) && File.Exists(compilation.PdbPath));
        Check("失败编译保留最近可用 g2 全部缓存与认证元数据摘要", CacheUnchanged(g2Cache, g2Fingerprint));
        await CheckRetainedPair("编译失败");
        SourcePluginFixture.Write(pluginsRoot, "g3", failInitialize: true);
        Check("候选初始化失败 reload 返回失败", !await runtime.ReloadAsync(SourcePluginFixture.Id));
        Check("候选初始化失败不切换当前 Bridge 与 g2", bridgeFactory.Scopes.Count == scopesBeforeFailure + 1
            && (await currentHandle.QuerySingleAsync(new("SELECT 1;")))?[0].Integer == 1
            && Modified(await sourceRequest.ExecuteAsync("storage"), "storage-ok:g2"));
        Check("失败候选 Bridge 已释放", await BridgeDisposed(bridgeFactory.Scopes.Last().Scope));
        Check("失败 reload 诊断含初始化与保留原实例阶段", runtime.StructuredDiagnostics.Any(item => item.Code == PluginDiagnosticCodes.InitializationFailed)
            && runtime.StructuredDiagnostics.Any(item => item.Code == PluginDiagnosticCodes.ReloadFailed));
        Check("初始化失败保留最近可用 g2 缓存摘要", CacheUnchanged(g2Cache, g2Fingerprint));
        await CheckRetainedPair("初始化失败");
        SourcePluginFixture.Write(pluginsRoot, "g2");
    });

    await Scenario("Credential 历史 Vault 与跨 runtime", async () =>
    {
        if (request is not IContextualPipeline contextual || response is null) throw new InvalidOperationException();
        var metadata = new Dictionary<string, string> { ["api_mode"] = "openai" };
        var payload = RequestBody(secret);
        var protectedResult = await contextual.ExecuteAsync(payload, metadata);
        issuedToken = Regex.Match(protectedResult.Payload, Regex.Escape("{{LOOMX_CREDENTIAL_") + "[A-Z2-7]{20}" + Regex.Escape("}}")).Value;
        Check("请求正文凭据脱敏并签发长期 token", protectedResult.Outcome == PipelineOutcome.Modified
            && !protectedResult.Payload.Contains(secret, StringComparison.Ordinal) && issuedToken.Length > 0);
        Check("placeholder 完整性指令临时注入 system/developer", IntegrityPresent(protectedResult.Payload));
        Check("原始客户端请求未被改写", payload == RequestBody(secret));
        const string businessPayload = """{"model":"example-model","status":"ready"}""";
        var business = await request.ExecuteAsync(businessPayload);
        Check("普通业务请求原样通过", business.Outcome == PipelineOutcome.Passed && business.Payload == businessPayload);
        Check("动态响应 Pipeline 恢复新 token", Modified(await response.ExecuteAsync(ResponseBody(issuedToken)), ResponseBody(secret)));
        Check("Bridge 直接恢复预置旧格式 Vault token", Modified(await response.ExecuteAsync(ResponseBody(LegacyVaultFixture.Token)), ResponseBody(LegacyVaultFixture.Secret)));
        var legacyRequest = await contextual.ExecuteAsync(RequestBody(LegacyVaultFixture.Secret), metadata);
        Check("旧 Vault OriginalHash 稳定复用原 token", legacyRequest.Payload.Contains(LegacyVaultFixture.Token, StringComparison.Ordinal)
            && !legacyRequest.Payload.Contains(LegacyVaultFixture.Secret, StringComparison.Ordinal));
        runtime.SetEntryEnabled(LegacyVaultFixture.PluginId, "credential.request", false);
        var disabled = await request.ExecuteAsync(payload);
        Check("Credential Entry 禁用后跳过新脱敏", disabled.Outcome == PipelineOutcome.Passed && disabled.Payload == payload);
        runtime.SetEntryEnabled(LegacyVaultFixture.PluginId, "credential.request", true);
        Check("Credential Entry 重新启用恢复脱敏", (await contextual.ExecuteAsync(payload, metadata)).Payload.Contains(issuedToken, StringComparison.Ordinal));
        runtime.SetPluginEnabled(LegacyVaultFixture.PluginId, false);
        var paused = await request.ExecuteAsync(payload);
        Check("暂停主动保护停止新明文 token 化", paused.Outcome == PipelineOutcome.Passed && paused.Payload == payload);
        Check("暂停主动保护仍为历史 token 注入完整性指令", IntegrityPresent((await contextual.ExecuteAsync(RequestBody(LegacyVaultFixture.Token), metadata)).Payload));
        Check("暂停主动保护保留 Vault 历史恢复能力", Modified(await response.ExecuteAsync(ResponseBody(LegacyVaultFixture.Token)), ResponseBody(LegacyVaultFixture.Secret)));
        runtime.SetPluginEnabled(LegacyVaultFixture.PluginId, true);
        var credentialScopes = bridgeFactory.Scopes.Where(item => item.Id == LegacyVaultFixture.PluginId).Select(item => item.Scope).ToArray();
        await runtime.DisposeAsync();
        Check("第一个 runtime 退休释放 Credential Bridge", credentialScopes.Length == 1 && await BridgeDisposed(credentialScopes[0]));
        await using var reopened = PluginRuntime.Start(Options(), loggerFactory);
        Check("新 runtime 同时重新加载源码与预编译插件且无诊断", reopened.PluginInfos.Count == 2 && reopened.Diagnostics.Count == 0);
        var reopenedRequest = (IContextualPipeline)reopened.GetPipeline("request")!;
        var reopenedResponse = reopened.GetPipeline("response")!;
        Check("跨 runtime 重开 Vault 恢复原历史 token", Modified(await reopenedResponse.ExecuteAsync(ResponseBody(LegacyVaultFixture.Token)), ResponseBody(LegacyVaultFixture.Secret)));
        Check("跨 runtime 重开 Vault 恢复前代签发 token", Modified(await reopenedResponse.ExecuteAsync(ResponseBody(issuedToken)), ResponseBody(secret)));
        Check("跨 runtime 相同凭据继续复用原 token", (await reopenedRequest.ExecuteAsync(RequestBody(secret), metadata)).Payload.Contains(issuedToken, StringComparison.Ordinal));
        await using var inspect = new PluginSqliteStorageBridge(LegacyVaultFixture.PluginId, dataRoot);
        await using var database = await inspect.OpenSqliteAsync("credential-tokens.db");
        var row = await database.QuerySingleAsync(new("SELECT ProtectedOriginal,CreatedAtUtc FROM CredentialTokens WHERE Token=$token;",
            [new("$token", PluginSqlValue.FromText(LegacyVaultFixture.Token))]));
        Check("历史记录 DPAPI 密文与创建时间原样保留", row?[0].Text == protectedLegacy && row?[1].Text == "2026-01-01T00:00:00.0000000+00:00");
        var rows = await database.QueryAsync(new("SELECT ProtectedOriginal FROM CredentialTokens;"));
        Check("Vault 只含两条稳定映射且不存明文", rows.Count == 2 && rows.All(item => item[0].Text?.StartsWith("dpapi:v1:", StringComparison.Ordinal) == true
            && !item[0].Text!.Contains(secret, StringComparison.Ordinal) && !item[0].Text!.Contains(LegacyVaultFixture.Secret, StringComparison.Ordinal)));
        Check("Credential 仅使用原 credential-tokens.db，不创建第二份 Vault", Directory.EnumerateFiles(Path.Combine(dataRoot, LegacyVaultFixture.PluginId), "*.db", SearchOption.AllDirectories)
            .Select(Path.GetFileName).SequenceEqual(new[] { "credential-tokens.db" }));
        Check("跨 runtime 源码 SQLite 数据可重新打开", Modified(await reopened.GetPipeline(SourcePluginFixture.Request)!.ExecuteAsync("storage"), "storage-ok:g2"));
        var activeScopes = bridgeFactory.Scopes.TakeLast(2).Select(item => item.Scope).ToArray();
        await reopened.DisposeAsync();
        Check("第二个 runtime 所有能力作用域已退休", activeScopes.Length == 2 && await BridgeDisposed(activeScopes[0]) && await BridgeDisposed(activeScopes[1]));
    });
    Check("编译/运行诊断不回显源码敏感字面量", !string.Join(";", runtime.StructuredDiagnostics).Contains(SourcePluginFixture.Sentinel, StringComparison.Ordinal));
});

await Scenario("原有 Pipeline 顺序及失败策略", async () =>
{
    var executionOrder = new List<string>();
    var pipeline = new Pipeline("request-order", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
    var first = new PipelineEntry(new DemoRequestExtension("first", executionOrder), "demo.first");
    var second = new PipelineEntry(new DemoRequestExtension("second", executionOrder), "demo.second");
    pipeline.AddEntry(first); pipeline.AddEntry(second); pipeline.Reorder([second, first]);
    await pipeline.ExecuteAsync("payload");
    Check("Pipeline Entry 按 Router 配置顺序执行", executionOrder.SequenceEqual(["second", "first"]));
    var resilient = new Pipeline("request-resilient", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
    resilient.AddEntry(new(new ThrowingRequestExtension("observability", ExtensionFailurePolicy.ContinueOnError), "demo.observability"));
    resilient.AddEntry(new(new AppendRequestExtension("tail"), "demo.tail"));
    Check("普通插件异常隔离且主流程继续", Modified(await resilient.ExecuteAsync("payload"), "payload-tail"));
    var failClosed = new Pipeline("request-secure", ExtensionKind.Request, loggerFactory.CreateLogger<Pipeline>());
    failClosed.AddEntry(new(new ThrowingRequestExtension("safety", ExtensionFailurePolicy.FailClosed), "demo.safety"));
    var blocked = await failClosed.ExecuteAsync(RequestBody(secret));
    Check("数据安全插件异常 fail closed", blocked.Outcome == PipelineOutcome.Blocked && string.IsNullOrEmpty(blocked.Payload));
});
var captured = string.Join(";", capture.Events.Select(item => item.Message)) + JsonSerializer.Serialize(bridgeFactory.Logs.Select(item => new { item.Id, item.Entry }))
    + JsonSerializer.Serialize(bridgeFactory.Notifications.Select(item => new { item.Id, item.Notification }));
Check("宿主日志与捕获通知均不含凭据/正文/token 哨兵", new[] { secret, LegacyVaultFixture.Secret, SourcePluginFixture.Sentinel, LegacyVaultFixture.Token,
    RequestBody(secret), ResponseBody(secret) }.All(value => !captured.Contains(value, StringComparison.Ordinal))
    && (string.IsNullOrEmpty(issuedToken) || !captured.Contains(issuedToken, StringComparison.Ordinal)));
Check("宿主捕获源码编译开始/完成/失败事件", capture.Events.Any(item => item.Message.Contains("编译开始", StringComparison.Ordinal))
    && capture.Events.Any(item => item.Message.Contains("编译完成", StringComparison.Ordinal))
    && capture.Events.Any(item => item.Message.Contains("编译失败", StringComparison.Ordinal)));
Check("宿主捕获 SQLite 完成与 reload 完成事件", capture.Events.Any(item => item.Message.Contains("数据库操作完成", StringComparison.Ordinal))
    && capture.Events.Any(item => item.Message.Contains("重新加载完成", StringComparison.Ordinal)));
Console.Out.WriteLine($"PluginPlayground：{checks} 项检查，{checks - failures.Count} 项通过，{failures.Count} 项失败。");
if (failures.Count > 0) Console.Out.WriteLine("失败项：" + string.Join("、", failures));
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

internal sealed class DemoRequestExtension(string extensionId, List<string> log) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken)
    {
        log.Add(extensionId);
        return ValueTask.FromResult(PipelineResult.Pass(payload));
    }
}

internal sealed class AppendRequestExtension(string extensionId) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.ContinueOnError;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(PipelineResult.Modify(payload + "-tail"));
}

internal sealed class ThrowingRequestExtension(
    string extensionId,
    ExtensionFailurePolicy failurePolicy) : IRequestExtension
{
    public string ExtensionId => extensionId;
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => failurePolicy;
    public IReadOnlyList<string> Capabilities => [];

    public ValueTask<PipelineResult> ProcessRequestAsync(
        PipelineContext context,
        string payload,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Playground 模拟异常。");
}
