using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class CredentialStorageBridgeTests : IDisposable
{
    private const string TokenA = "{{LOOMX_CREDENTIAL_AAAAAAAAAAAAAAAAAAAA}}";
    private const string TokenB = "{{LOOMX_CREDENTIAL_BBBBBBBBBBBBBBBBBBBB}}";
    private readonly CredentialTestFactory factory = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "loomx-credential-bridge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task NewVault_PreservesLegacyInitializationSchemaAndIndexes()
    {
        var legacyDirectory = Path.Combine(directory, "legacy");
        var newDirectory = Path.Combine(directory, "new");
        _ = new CredentialLegacyVault(legacyDirectory);
        var storage = factory.CreateStorage(newDirectory);
        var probe = new CredentialBridgeProbe(storage);
        await using var store = new CredentialTokenStore(probe);
        await store.InitializeAsync();
        Assert.Equal(["credential-tokens.db"], probe.Paths.ToArray());
        Assert.Equal(new[] { "PRAGMA journal_mode=WAL;", "PRAGMA synchronous=NORMAL;", "PRAGMA busy_timeout=5000;" },
            probe.Commands.Take(3).Select(item => item.Command.Text));
        var legacySchema = await SchemaAsync(factory.CreateStorage(legacyDirectory));
        Assert.Equal(legacySchema, await SchemaAsync(storage));
        Assert.Contains("Token", legacySchema, StringComparison.Ordinal);
        Assert.Contains("OriginalHash", legacySchema, StringComparison.Ordinal);
        Assert.Contains("ProtectedOriginal", legacySchema, StringComparison.Ordinal);
        Assert.Contains("CreatedAtUtc", legacySchema, StringComparison.Ordinal);
        Assert.Equal(2, (await IndexesAsync(storage)).Count);
        Assert.Equal("credential-tokens.db", Assert.Single(Directory.GetFiles(newDirectory, "*.db")).Split(Path.DirectorySeparatorChar).Last());
    }

    [Fact]
    public async Task Store_ReusesStableTokenAcrossRequestsAndDeduplicatesExactRecovery()
    {
        var storage = factory.CreateStorage(directory);
        var probe = new CredentialBridgeProbe(storage);
        await using var first = new CredentialTokenStore(probe);
        const string original = "sk-bridge-stable-abcdefghij0123456789";
        var token = await first.GetOrCreateTokenAsync(original, () => TokenA);
        Assert.Equal(token, await first.GetOrCreateTokenAsync(original, () => throw new InvalidOperationException("不应重签发。")));
        await using var second = factory.CreateStore(directory);
        Assert.Equal(token, await second.GetOrCreateTokenAsync(original, () => TokenB));
        var before = probe.Commands.Count(item => item.Command.Text.Contains("WHERE Token =", StringComparison.Ordinal));
        var restored = await first.ResolveTokensAsync([token, token, token]);
        Assert.Equal(original, Assert.Single(restored).Value);
        Assert.Equal(before + 1, probe.Commands.Count(item => item.Command.Text.Contains("WHERE Token =", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LegacyRealVault_RecoversWithoutMigrationOrCiphertextHashChanges()
    {
        const string original = "旧凭据\\路径\"引号\"\n";
        var legacy = new CredentialLegacyVault(directory);
        var token = legacy.GetOrCreateToken(original, () => TokenA);
        var storage = factory.CreateStorage(directory);
        var before = await RowsAsync(storage);
        var protectedOriginal = Assert.Single(before)[2].Text!;
        Assert.StartsWith("dpapi:v1:", protectedOriginal, StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))), before[0][1].Text);
        await using var upgraded = new CredentialTokenStore(storage);
        Assert.Equal(original, (await upgraded.ResolveTokensAsync([token]))[token]);
        Assert.Equal(token, await upgraded.GetOrCreateTokenAsync(original, () => TokenB));
        var after = await RowsAsync(storage);
        Assert.Equal(before[0].Values, after[0].Values);
        Assert.Equal(original, legacy.ResolveTokens([token])[token]);
        Assert.Equal(["credential-tokens.db"], Directory.GetFiles(directory, "*.db").Select(Path.GetFileName));
    }

    [Fact]
    public async Task ConcurrentOriginalHashConflict_DisposesFailedTransactionBeforeExactRequery()
    {
        var probes = new[] { new CredentialBridgeProbe(factory.CreateStorage(directory)), new CredentialBridgeProbe(factory.CreateStorage(directory)) };
        await using var first = new CredentialTokenStore(probes[0]);
        await using var second = new CredentialTokenStore(probes[1]);
        await first.InitializeAsync();
        await second.InitializeAsync();
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        foreach (var probe in probes)
            probe.After = async (operation, command, _) =>
            {
                if (operation != "query-single" || command?.Text.Contains("WHERE OriginalHash", StringComparison.Ordinal) != true) return;
                if (Interlocked.Increment(ref reads) > 2) return;
                if (Volatile.Read(ref reads) == 2) bothRead.TrySetResult();
                await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            };
        var tokens = await Task.WhenAll(
            first.GetOrCreateTokenAsync("同一并发凭据", () => TokenA).AsTask(),
            second.GetOrCreateTokenAsync("同一并发凭据", () => TokenB).AsTask()).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(tokens[0], tokens[1]);
        Assert.Single(await RowsAsync(factory.CreateStorage(directory)));
        var loser = Assert.Single(probes, probe => probe.Events.Contains("transaction.rollback"));
        var events = loser.Events.ToArray();
        var rollback = Array.IndexOf(events, "transaction.rollback");
        Assert.Equal("transaction.dispose", events[rollback + 1]);
        Assert.Equal("query-single", events[rollback + 2]);
        Assert.DoesNotContain(CredentialEngine.PlaceholderPrefix, tokens[0][CredentialEngine.PlaceholderPrefix.Length..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenCollision_RechecksHashAfterDisposalThenUsesFreshTransaction()
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        await using var store = new CredentialTokenStore(probe);
        await store.GetOrCreateTokenAsync("第一段凭据", () => TokenA);
        var attempts = 0;
        var token = await store.GetOrCreateTokenAsync("第二段凭据", () => ++attempts == 1 ? TokenA : TokenB);
        Assert.Equal(TokenB, token);
        Assert.Equal(2, attempts);
        var events = probe.Events.ToArray();
        var rollback = Array.IndexOf(events, "transaction.rollback");
        Assert.Equal(new[] { "transaction.dispose", "query-single", "begin" }, events.Skip(rollback + 1).Take(3));
        var restored = await store.ResolveTokensAsync([TokenA, TokenB]);
        Assert.Equal("第一段凭据", restored[TokenA]);
        Assert.Equal("第二段凭据", restored[TokenB]);
    }

    [Fact]
    public async Task CancelledAndDisposedStore_NeverOpensOrRecreatesVault()
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        var store = new CredentialTokenStore(probe);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetOrCreateTokenAsync("凭据", () => TokenA, cancelled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ResolveTokensAsync([], cancelled.Token).AsTask());
        await store.DisposeAsync();
        await store.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ResolveTokensAsync([]).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.GetOrCreateTokenAsync("凭据", () => TokenA).AsTask());
        Assert.Empty(probe.Paths);
        Assert.False(File.Exists(Path.Combine(directory, "credential-tokens.db")));
    }

    [Fact]
    public async Task CancellationDuringTransaction_RollsBackWithoutIssuingTokenAndStoreRemainsUsable()
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        await using var store = new CredentialTokenStore(probe);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Before = async (operation, _, token) =>
        {
            if (operation != "transaction.commit") return;
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        using var cancellation = new CancellationTokenSource();
        var create = store.GetOrCreateTokenAsync("中途取消凭据", () => TokenA, cancellation.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => create);
        probe.Before = null;
        Assert.Empty(await store.ResolveTokensAsync([TokenA]));
        Assert.Equal(TokenB, await store.GetOrCreateTokenAsync("中途取消凭据", () => TokenB));
        Assert.Contains("transaction.dispose", probe.Events);
    }

    [Fact]
    public async Task Disposal_CancelsInflightOperationsAndReleasesOnlyOwnedHandle()
    {
        var storage = factory.CreateStorage(directory);
        var probe = new CredentialBridgeProbe(storage);
        var store = new CredentialTokenStore(probe);
        await store.GetOrCreateTokenAsync("释放凭据", () => TokenA);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Before = async (operation, _, token) =>
        {
            if (operation != "query-single") return;
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var resolve = store.ResolveTokensAsync([TokenA]).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolve);
        await store.DisposeAsync();
        Assert.Equal(1, probe.Events.Count(item => item == "database.dispose"));
        await using var stillOwnedByHost = await storage.OpenSqliteAsync("host-still-usable.db");
        Assert.NotNull(stillOwnedByHost);
    }

    [Theory]
    [InlineData(PluginSqlError.Busy)]
    [InlineData(PluginSqlError.Locked)]
    [InlineData(PluginSqlError.Corrupt)]
    [InlineData(PluginSqlError.Disposed)]
    public async Task BridgeFailures_BlockRequestsAndResponsesWithoutLeakingOriginal(PluginSqlError error)
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        await using var store = new CredentialTokenStore(probe);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        const string original = "sk-failure-abcdefghij0123456789";
        var issued = engine.Sanitize(original, out _);
        probe.Before = (operation, _, _) => operation == "query-single"
            ? ValueTask.FromException(new PluginSqlException(error, "受控存储失败。")) : ValueTask.CompletedTask;
        var request = await new CredentialRequestExtension(engine).ProcessRequestAsync(new PipelineContext("request"), original, CancellationToken.None);
        var response = await new CredentialResponseExtension(engine).ProcessResponseAsync(new PipelineContext("response"), issued, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, request.Outcome);
        Assert.Equal(PipelineOutcome.Blocked, response.Outcome);
        Assert.Empty(request.Payload);
        Assert.Empty(response.Payload);
        Assert.DoesNotContain(original, request.Diagnostic ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("明文非法前缀")]
    [InlineData("dpapi:v1:不是Base64")]
    [InlineData("dpapi:v1:AAAAAAAAAAAAAAAAAAAAAA==")]
    public async Task InvalidDpapiCiphertext_BlocksRecovery(string protectedOriginal)
    {
        var legacy = new CredentialLegacyVault(directory);
        legacy.GetOrCreateToken("不可解密凭据", () => TokenA);
        var storage = factory.CreateStorage(directory);
        await using (var db = await storage.OpenSqliteAsync("credential-tokens.db"))
            await db.ExecuteAsync(new PluginSqlCommand("UPDATE CredentialTokens SET ProtectedOriginal = $value WHERE Token = $token;",
                [new("$value", PluginSqlValue.FromText(protectedOriginal)), new("$token", PluginSqlValue.FromText(TokenA))]));
        await using var store = new CredentialTokenStore(storage);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        var response = await new CredentialResponseExtension(engine).ProcessResponseAsync(new PipelineContext("response"), TokenA, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, response.Outcome);
        Assert.Empty(response.Payload);
    }

    [Fact]
    public async Task UnknownAndDamagedReferences_AreNeverFuzzilyResolved()
    {
        await using var store = factory.CreateStore(directory);
        await store.GetOrCreateTokenAsync("不能猜测的凭据", () => TokenA);
        Assert.Empty(await store.ResolveTokensAsync([TokenB]));
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        foreach (var reference in new[] { TokenB, TokenA.Replace("LOOMX", "LＯOMX", StringComparison.Ordinal), TokenA[..^3] })
        {
            Assert.Equal(reference, engine.Restore(reference, out var changed));
            Assert.False(changed);
        }
    }

    [Fact]
    public async Task SyncJsonAndSseParsing_OnUiContextDoesNotPostOrDeadlock()
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        probe.Before = async (_, _, _) => { await Task.Yield(); };
        await using var store = new CredentialTokenStore(probe);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        var context = new NonPumpingContext();
        var completed = new TaskCompletionSource<(string Json, string Sse)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var issued = engine.Sanitize("sk-ui-abcdefghij0123456789", out _);
                completed.TrySetResult((engine.RestoreTransportPayload(JsonSerializer.Serialize(new { content = issued }), out _),
                    engine.RestoreTransportPayload("data: " + JsonSerializer.Serialize(new { content = issued }) + "\n\n", out _)));
            }
            catch (Exception exception) { completed.TrySetException(exception); }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("sk-ui-abcdefghij0123456789", result.Json, StringComparison.Ordinal);
            Assert.Contains("sk-ui-abcdefghij0123456789", result.Sse, StringComparison.Ordinal);
            Assert.Empty(context.Posted);
        }
        finally { context.Drain(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledPipelineCall_FailsClosedBeforeProcessing(bool response)
    {
        var engine = factory.CreateEngine(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = response
            ? await new CredentialResponseExtension(engine).ProcessResponseAsync(new PipelineContext("response"), TokenA, cancellation.Token)
            : await new CredentialRequestExtension(engine).ProcessRequestAsync(new PipelineContext("request"), "sk-cancel-abcdefghij0123456789", cancellation.Token);
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
    }

    [Fact]
    public async Task ResponsePipeline_ForwardsInflightCancellationIntoStorage()
    {
        var probe = new CredentialBridgeProbe(factory.CreateStorage(directory));
        await using var store = new CredentialTokenStore(probe);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        var issued = engine.Sanitize("sk-cancel-forward-abcdefghij0123456789", out _);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Before = async (operation, _, token) =>
        {
            if (operation != "query-single") return;
            started.TrySetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
        };
        using var cancellation = new CancellationTokenSource();
        var processing = Task.Run(async () => await new CredentialResponseExtension(engine).ProcessResponseAsync(
            new PipelineContext("response"), issued, cancellation.Token));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        release.TrySetResult();
        var result = await processing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
    }

    [Fact]
    public async Task PluginDisable_PreservesHistoryAndRetirementFailsClosedWithoutDeletingVault()
    {
        await using var plugin = new CredentialProtectionPlugin();
        plugin.Initialize(factory.CreateContext(directory));
        var extensions = plugin.CreateExtensions().ToArray();
        const string original = "sk-retirement-abcdefghij0123456789";
        var issued = plugin.Engine!.Sanitize(original, out _);
        plugin.OnPluginEnabledChanged(false);
        var response = extensions.OfType<CredentialResponseExtension>().Single();
        var restored = await response.ProcessResponseAsync(new PipelineContext("response"), issued, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Modified, restored.Outcome);
        Assert.Equal(original, restored.Payload);
        await plugin.DisposeAsync();
        var retired = await response.ProcessResponseAsync(new PipelineContext("response"), issued, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, retired.Outcome);
        Assert.Empty(retired.Payload);
        Assert.True(File.Exists(Path.Combine(directory, "credential-tokens.db")));
        var reloaded = factory.CreatePlugin(directory);
        Assert.Equal(original, reloaded.Engine!.Restore(issued, out var changed));
        Assert.True(changed);
    }

    [Fact]
    public async Task CorruptRealVault_BlocksRequestAndDoesNotCreateReplacementDatabase()
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "credential-tokens.db");
        var broken = Encoding.UTF8.GetBytes("这是损坏的旧 Vault，不能静默重建。");
        await File.WriteAllBytesAsync(file, broken);
        await using var store = factory.CreateStore(directory);
        var engine = new CredentialEngine(new SensitiveRuleStore(directory), store);
        var result = await new CredentialRequestExtension(engine).ProcessRequestAsync(
            new PipelineContext("request"), "sk-corrupt-abcdefghij0123456789", CancellationToken.None);
        Assert.Equal(PipelineOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Payload);
        Assert.Equal(broken, await File.ReadAllBytesAsync(file));
        Assert.Equal(["credential-tokens.db"], Directory.GetFiles(directory, "*.db").Select(Path.GetFileName));
    }

    [Fact]
    public async Task BridgeLogs_DoNotContainPlaintextTokensHashesCiphertextsOrSql()
    {
        var logger = new CredentialBridgeLogger();
        var storage = factory.CreateStorage(directory, logger);
        await using var store = new CredentialTokenStore(storage);
        const string first = "sk-log-first-abcdefghij0123456789";
        const string second = "sk-log-second-abcdefghij0123456789";
        await store.GetOrCreateTokenAsync(first, () => TokenA);
        var attempts = 0;
        await store.GetOrCreateTokenAsync(second, () => ++attempts == 1 ? TokenA : TokenB);
        await store.ResolveTokensAsync([TokenA, TokenB]);
        var rows = await RowsAsync(storage);
        var log = string.Join("\n", logger.Entries);
        Assert.Contains("Information:", log, StringComparison.Ordinal);
        Assert.Contains("Warning:", log, StringComparison.Ordinal);
        foreach (var sensitive in new[] { first, second, TokenA, TokenB, "SELECT Token", "INSERT INTO CredentialTokens" })
            Assert.DoesNotContain(sensitive, log, StringComparison.Ordinal);
        foreach (var row in rows)
        {
            Assert.DoesNotContain(row[1].Text!, log, StringComparison.Ordinal);
            Assert.DoesNotContain(row[2].Text!, log, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CancelledRequest_DoesNotPoisonOtherCallsOnSharedEngine()
    {
        var engine = factory.CreateEngine(directory);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var request = new CredentialRequestExtension(engine);
        const string original = "sk-scope-abcdefghij0123456789";
        var failed = await request.ProcessRequestAsync(new PipelineContext("request"), original, cancelled.Token);
        Assert.Equal(PipelineOutcome.Blocked, failed.Outcome);
        var succeeded = await request.ProcessRequestAsync(new PipelineContext("request"), original, CancellationToken.None);
        Assert.Equal(PipelineOutcome.Modified, succeeded.Outcome);
        Assert.Equal(original, engine.Restore(succeeded.Payload, out var changed));
        Assert.True(changed);
    }

    private static async Task<string> SchemaAsync(IPluginStorageBridge storage)
    {
        await using var db = await storage.OpenSqliteAsync("credential-tokens.db");
        var rows = await db.QueryAsync(new PluginSqlCommand("SELECT name, sql FROM sqlite_master WHERE type IN ('table','index') ORDER BY name;"));
        return string.Join("|", rows.Select(row => row[0].Text + ":" + row[1].Text)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<PluginSqlRow>> IndexesAsync(IPluginStorageBridge storage)
    {
        await using var db = await storage.OpenSqliteAsync("credential-tokens.db");
        return await db.QueryAsync(new PluginSqlCommand("PRAGMA index_list(CredentialTokens);"));
    }

    private static async Task<IReadOnlyList<PluginSqlRow>> RowsAsync(IPluginStorageBridge storage)
    {
        await using var db = await storage.OpenSqliteAsync("credential-tokens.db");
        return await db.QueryAsync(new PluginSqlCommand("SELECT Token, OriginalHash, ProtectedOriginal, CreatedAtUtc FROM CredentialTokens ORDER BY Token;"));
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        internal ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Posted { get; } = new();
        public override void Post(SendOrPostCallback d, object? state) => Posted.Enqueue((d, state));
        internal void Drain() { while (Posted.TryDequeue(out var work)) work.Callback(work.State); }
    }
}
