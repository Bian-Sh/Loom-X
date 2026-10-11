using System.Globalization;
using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 设置页交互：命令由宿主回传，插件自行校验、落库并刷新快照。
/// </summary>
public sealed partial class ToolResultCompressionPlugin : IPluginUiCommandHandler
{
    public async ValueTask<PluginUiCommandResult> ExecuteUiCommandAsync(
        PluginUiCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var store = optionsStore;
        if (store is null)
            return PluginUiCommandResult.Failure("PLUGIN_NOT_INITIALIZED");

        var result = await CompressionSettingsCommands
            .Execute(store, observability, command, CultureInfo.CurrentUICulture.Name)
            .ConfigureAwait(false);

        // 配置或统计变化后让宿主重新拉取快照，UI 才能反映新值。
        if (result.Handled) observability.RaiseChanged();
        return result;
    }
}
