namespace LoomX.Plugins;

/// <summary>
/// 宿主把插件声明式 UI 上的一次交互回传给插件的命令。
/// Arguments 由「命令自带参数」与「当前表单字段值」合并而成，字段值以 FieldId 为键；
/// 两者键冲突时命令自带参数优先，插件不得依赖冲突行为。
/// </summary>
public sealed record PluginUiCommand(
    string ContributionId,
    string CommandId,
    IReadOnlyDictionary<string, string> Arguments)
{
    public static PluginUiCommand Create(
        string contributionId,
        string commandId,
        IReadOnlyDictionary<string, string>? arguments = null) =>
        new(contributionId, commandId, arguments ?? new Dictionary<string, string>(StringComparer.Ordinal));

    public string GetArgument(string key) =>
        Arguments.TryGetValue(key, out var value) ? value : string.Empty;
}

/// <summary>
/// 命令执行结果。Message 会由宿主呈现给用户（Toast），
/// 因此插件 MUST 只返回安全摘要，不得包含凭据、密钥、请求/响应正文或用户内容。
/// </summary>
public sealed record PluginUiCommandResult(
    bool Handled,
    string? Message = null,
    PluginUiTone Tone = PluginUiTone.Default)
{
    public static PluginUiCommandResult Unhandled => new(false);

    public static PluginUiCommandResult Success(string? message = null) =>
        new(true, message, PluginUiTone.Success);

    public static PluginUiCommandResult Failure(string? message = null) =>
        new(true, message, PluginUiTone.Danger);

    public static PluginUiCommandResult Notice(string? message = null, PluginUiTone tone = PluginUiTone.Default) =>
        new(true, message, tone);
}

/// <summary>
/// 插件可选实现的声明式 UI 命令处理器。宿主只转发命令标识与参数，
/// 由插件自行校验、落库并通过 <see cref="IPluginUiContributionProvider.UiInvalidated"/> 触发快照刷新。
/// </summary>
public interface IPluginUiCommandHandler
{
    ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(
        PluginUiCommand command,
        CancellationToken cancellationToken = default);
}
