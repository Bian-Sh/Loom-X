using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 第一方 Tool Result Compression 插件：在请求正文发送给 Provider 前压缩历史工具调用结果。
/// 只处理工具结果文本，保留最近若干条原文；压缩失败放行原文（非数据安全能力，不 fail closed）。
/// <para>
/// 按职责拆分为多个 partial：本文件是生命周期与扩展注册，
/// UI 快照在 <c>ToolResultCompressionPlugin.Ui.cs</c>，命令处理在
/// <c>ToolResultCompressionPlugin.Commands.cs</c>。
/// </para>
/// </summary>
public sealed partial class ToolResultCompressionPlugin : ILoomXPlugin
{
    public const string PluginId = "loomx.tool-result-compression";

    private readonly ToolResultCompressionObservability observability = new();
    private ToolResultCompressionOptionsStore? optionsStore;
    private CompressionStatsStore? statsStore;

    public string Id => PluginId;

    /// <summary>观测状态（跨会话累计，绑定统计存储后自动载入历史值）。</summary>
    public ToolResultCompressionObservability Observability => observability;

    /// <summary>配置存储（宿主 Initialize 后可用；测试也可直接注入）。</summary>
    public ToolResultCompressionOptionsStore OptionsStore =>
        optionsStore ?? throw new InvalidOperationException("插件尚未初始化。");

    public void Initialize(PluginInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        optionsStore = new ToolResultCompressionOptionsStore(context.DataDirectory);
        optionsStore.Load();

        statsStore = new CompressionStatsStore(context.DataDirectory);
        observability.AttachStore(statsStore);
    }

    public IEnumerable<IPipelineExtension> CreateExtensions()
    {
        // 宿主保证 Initialize 先于 CreateExtensions 调用；防御性兜底避免空引用。
        var store = optionsStore ?? new ToolResultCompressionOptionsStore(
            Path.Combine(Path.GetTempPath(), PluginId));
        yield return new ToolResultCompressionRequestExtension(store, observability);
    }
}
