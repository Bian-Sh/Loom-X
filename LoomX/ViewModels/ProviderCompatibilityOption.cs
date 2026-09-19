namespace LoomX.ViewModels;

public sealed record ProviderCompatibilityOption(
    string Value,
    string ApiMode,
    string EndpointFormat,
    string TitleKey,
    string DescriptionKey)
{
    public static ProviderCompatibilityOption OpenAiChat { get; } = new(
        "openai-chat",
        "openai",
        "chat_completions",
        "providers.compat.chat.title",
        "providers.compat.chat.description");

    public static ProviderCompatibilityOption OpenAiResponses { get; } = new(
        "openai-responses",
        "openai",
        "responses",
        "providers.compat.responses.title",
        "providers.compat.responses.description");

    public static ProviderCompatibilityOption AnthropicMessages { get; } = new(
        "anthropic-messages",
        "anthropic",
        "responses",
        "providers.compat.anthropic.title",
        "providers.compat.anthropic.description");

    public static IReadOnlyList<ProviderCompatibilityOption> All { get; } =
        [OpenAiChat, OpenAiResponses, AnthropicMessages];

    public static ProviderCompatibilityOption FromFields(string apiMode, string endpointFormat)
    {
        var matched = All.FirstOrDefault(option =>
            string.Equals(option.ApiMode, apiMode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(option.EndpointFormat, endpointFormat, StringComparison.OrdinalIgnoreCase));
        if (matched is not null) return matched;

        if (string.Equals(apiMode, "anthropic", StringComparison.OrdinalIgnoreCase)) return AnthropicMessages;
        return string.Equals(endpointFormat, "chat_completions", StringComparison.OrdinalIgnoreCase)
            ? OpenAiChat
            : OpenAiResponses;
    }

    public void ApplyTo(ProviderEditorViewModel provider)
    {
        provider.ApiMode = ApiMode;
        provider.EndpointFormat = EndpointFormat;
    }
}
