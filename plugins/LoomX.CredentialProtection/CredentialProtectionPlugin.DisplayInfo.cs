using LoomX.Plugins;

namespace LoomX.CredentialProtection;

/// <summary>
/// 插件自带展示信息。宿主资源里没有本插件的任何文案，
/// 名称、简介、分类与启停说明都按宿主传入的文化从插件自有资源返回。
/// </summary>
public sealed partial class CredentialProtectionPlugin
{
    public PluginDisplayInfo? GetDisplayInfo(PluginUiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = PluginTextCatalog.For(
            typeof(CredentialProtectionPlugin),
            CredentialProtectionPlugin.PluginId,
            context.CultureName);
        return new PluginDisplayInfo(
            text.Get("plugin.name"),
            text.Get("plugin.description"),
            text.Get("plugin.category"),
            text.Get("plugin.lifecycle"));
    }
}
