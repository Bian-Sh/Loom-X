namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// 客户端配置适配器：把 LoomX 网关身份渲染进一个 AI Agent 客户端自己的配置文件。
/// 移植自 ai-switch 的 route_config adapters；语义对齐：
/// - 只改写自己管理的键，用户手写的其他配置原样保留；
/// - 既有配置解析失败时拒绝覆写（fail closed），绝不静默销毁；
/// - 托管条目可被重复写入原地更新（adoption），不产生重复条目。
/// </summary>
public interface IClientConfigAdapter
{
    ClientTargetDescriptor Descriptor { get; }

    /// <summary>该客户端的配置文件在 homeRoot 下的绝对路径。</summary>
    FileInfo ResolvePath(string homeRoot);

    /// <summary>
    /// 渲染并写入配置。existing 为 null 表示文件不存在。
    /// 纯渲染型适配器返回 RenderedContent，由 Service 统一备份落盘；
    /// codex 走 TomlDocumentService（自带备份与原子替换），只回报 BackupPath。
    /// </summary>
    Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken);

    ClientConfigInspection Inspect(FileInfo path, string? existing);

    /// <summary>检查前的读取钩子；codex 需要异步读取 TOML，其余客户端同步。</summary>
    Task<ClientConfigInspection> InspectAsync(FileInfo path, string? existing, CancellationToken cancellationToken)
        => Task.FromResult(Inspect(path, existing));
}
