using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ProviderCompatibilityOptionTests
{
    [Theory]
    [InlineData("openai", "chat_completions", "openai-chat")]
    [InlineData("openai", "responses", "openai-responses")]
    [InlineData("anthropic", "responses", "anthropic-messages")]
    public void FromFields_返回稳定兼容类型(string apiMode, string endpointFormat, string expected)
    {
        Assert.Equal(expected, ProviderCompatibilityOption.FromFields(apiMode, endpointFormat).Value);
    }

    [Theory]
    [InlineData("anthropic", "chat_completions", "anthropic-messages")]
    [InlineData("openai", "legacy", "openai-responses")]
    public void FromFields_旧字段组合回退到稳定兼容类型(string apiMode, string endpointFormat, string expected)
    {
        Assert.Equal(expected, ProviderCompatibilityOption.FromFields(apiMode, endpointFormat).Value);
    }

    [Theory]
    [InlineData(nameof(ProviderEditorViewModel.ApiMode), "anthropic")]
    [InlineData(nameof(ProviderEditorViewModel.EndpointFormat), "chat_completions")]
    public void 兼容字段变化会通知SelectedCompatibility(string propertyName, string value)
    {
        var provider = new ProviderEditorViewModel();
        var notifications = new List<string?>();
        provider.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        if (propertyName == nameof(ProviderEditorViewModel.ApiMode)) provider.ApiMode = value;
        else provider.EndpointFormat = value;

        Assert.Contains(nameof(ProviderEditorViewModel.SelectedCompatibility), notifications);
    }

    [Fact]
    public void All_包含固定顺序和精确元数据()
    {
        Assert.Equal(
        [
            new ProviderCompatibilityOption("openai-chat", "openai", "chat_completions", "providers.compat.chat.title", "providers.compat.chat.description"),
            new ProviderCompatibilityOption("openai-responses", "openai", "responses", "providers.compat.responses.title", "providers.compat.responses.description"),
            new ProviderCompatibilityOption("anthropic-messages", "anthropic", "responses", "providers.compat.anthropic.title", "providers.compat.anthropic.description")
        ],
        ProviderCompatibilityOption.All);
    }

    [Fact]
    public void 应用兼容类型不会修改ProviderId()
    {
        var provider = new ProviderEditorViewModel { BusinessId = "provider-fixed" };

        ProviderCompatibilityOption.OpenAiChat.ApplyTo(provider);

        Assert.Equal("provider-fixed", provider.BusinessId);
        Assert.Equal("openai", provider.ApiMode);
        Assert.Equal("chat_completions", provider.EndpointFormat);
    }
}
