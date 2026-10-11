using System.Globalization;
using LoomX.Plugins;

namespace LoomX.ToolResultCompression;

/// <summary>
/// 卡片区文案。全部来自插件自有本地化资源（<c>i18n/*.json</c>），
/// 只描述数量与比例，不含任何工具结果内容。
/// </summary>
internal sealed record ToolResultCompressionUiText(
    PluginTextCatalog Localization,
    string CompressedRequests,
    string CompressedResults,
    string SavedTokens,
    string SavedHint,
    string Errors,
    string ErrorsHint,
    string TokenUnit)
{
    public CultureInfo Culture => Localization.Culture;

    public static ToolResultCompressionUiText Resolve(string cultureName)
    {
        var localization = PluginTextCatalog.For(
            typeof(ToolResultCompressionUiText),
            ToolResultCompressionPlugin.PluginId,
            cultureName);
        return new(
            localization,
            localization.Get("card.requests"),
            localization.Get("card.results"),
            localization.Get("card.saved"),
            localization.Get("card.saved_hint"),
            localization.Get("card.errors"),
            localization.Get("card.errors_hint"),
            localization.Get("card.token_unit"));
    }
}
