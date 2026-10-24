using Microsoft.Extensions.Logging;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// 客户端配置写入服务：管理适配器注册表、home 目录解析与统一的
/// Read → Backup → Write 流程。写入目标由调用方（clientconfig.* 工具）给出
/// targetKey；homeRoot 缺省为真实用户主目录，测试可传入 fake 目录（如 .codextest）。
/// </summary>
public sealed class ClientConfigService
{
    public const string InvalidExistingMessage = "既有客户端配置非法，已拒绝覆写；请先修复或手动备份该文件。";

    private readonly Dictionary<string, IClientConfigAdapter> adapters;
    private readonly ILogger<ClientConfigService> logger;

    public ClientConfigService(Assistant.Configuration.ITomlDocumentService tomlDocumentService, ILogger<ClientConfigService> logger)
    {
        this.logger = logger;
        IClientConfigAdapter[] list =
        [
            new CodexConfigAdapter(tomlDocumentService),
            JsonAgentConfigAdapter.ClaudeCode(),
            JsonAgentConfigAdapter.GeminiCli(),
            JsonAgentConfigAdapter.Grok(),
            ZCodeConfigAdapter.Codex(),
            ZCodeConfigAdapter.Claude(),
            new OpenCodeConfigAdapter(),
            new OpenClawConfigAdapter(),
            new HermesConfigAdapter(),
        ];
        adapters = list.ToDictionary(adapter => adapter.Descriptor.TargetKey, StringComparer.OrdinalIgnoreCase);
    }

    public static string DefaultHomeRoot => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IReadOnlyList<ClientTargetDescriptor> Targets => adapters.Values.Select(adapter => adapter.Descriptor)
        .OrderBy(descriptor => descriptor.TargetKey, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public ClientTargetDescriptor? Find(string targetKey) =>
        adapters.TryGetValue(targetKey, out var adapter) ? adapter.Descriptor : null;

    public FileInfo ResolvePath(string targetKey, string? homeRoot)
    {
        var adapter = RequireAdapter(targetKey);
        return adapter.ResolvePath(ResolveHomeRoot(homeRoot));
    }

    public async Task<ClientConfigWriteOutcome> WriteAsync(
        string targetKey,
        string? homeRoot,
        ClientWriteInput input,
        CancellationToken cancellationToken = default)
    {
        var adapter = RequireAdapter(targetKey);
        var resolvedHome = ResolveHomeRoot(homeRoot);
        var path = adapter.ResolvePath(resolvedHome);
        if (path.Directory is { } directory)
        {
            Directory.CreateDirectory(directory.FullName);
        }

        var existing = File.Exists(path.FullName) ? await File.ReadAllTextAsync(path.FullName, cancellationToken) : null;
        var result = await adapter.WriteAsync(path, existing, input, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var backupPath = result.BackupPath;
        if (result.RenderedContent is not null && result.RenderedContent != existing)
        {
            if (existing is not null)
            {
                backupPath = ClientConfigFiles.BackupPathFor(path.FullName);
                await File.WriteAllTextAsync(backupPath, existing, ClientConfigFiles.Utf8NoBom, cancellationToken);
            }

            await File.WriteAllTextAsync(path.FullName, result.RenderedContent, ClientConfigFiles.Utf8NoBom, cancellationToken);
        }

        var written = result.RenderedContent is not null || result.Changed;
        logger.LogInformation(
            "客户端配置写入完成 {TargetKey} {ConfigPath} Changed {Changed} Backup {HasBackup} Models {ModelCount}",
            targetKey,
            path.FullName,
            written,
            backupPath is not null,
            input.Models.Count);

        return new ClientConfigWriteOutcome(
            adapter.Descriptor.TargetKey,
            path.FullName,
            backupPath,
            written,
            result.CatalogPath,
            adapter.Descriptor.RestartRequired,
            input.Models.Select(model => model.Id).ToArray());
    }

    public async Task<ClientConfigInspection> InspectAsync(
        string targetKey,
        string? homeRoot,
        CancellationToken cancellationToken = default)
    {
        var adapter = RequireAdapter(targetKey);
        var path = adapter.ResolvePath(ResolveHomeRoot(homeRoot));
        var existing = File.Exists(path.FullName) ? await File.ReadAllTextAsync(path.FullName, cancellationToken) : null;
        return await adapter.InspectAsync(path, existing, cancellationToken);
    }

    private static string ResolveHomeRoot(string? homeRoot)
    {
        var resolved = string.IsNullOrWhiteSpace(homeRoot) ? DefaultHomeRoot : homeRoot.Trim();
        return Path.GetFullPath(resolved);
    }

    private IClientConfigAdapter RequireAdapter(string targetKey)
    {
        if (!adapters.TryGetValue(targetKey, out var adapter))
        {
            throw new ArgumentException($"未知客户端标识 '{targetKey}'。可用：{string.Join(", ", adapters.Keys.Order(StringComparer.OrdinalIgnoreCase))}");
        }

        return adapter;
    }
}
