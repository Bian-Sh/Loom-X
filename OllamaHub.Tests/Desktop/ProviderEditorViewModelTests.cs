using OllamaHub.Configuration;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Desktop;

public sealed class ProviderEditorViewModelTests
{
    [Fact]
    public void FromResponse_PreservesProviderEnabledState()
    {
        var response = new ProviderResponse(
            Guid.NewGuid(),
            "disabled-provider",
            "已停用 Provider",
            "https://example.com",
            "openai",
            true,
            false,
            false,
            0,
            "{}",
            [],
            null,
            "responses",
            null);

        var viewModel = ProviderEditorViewModel.FromResponse(response);

        Assert.True(viewModel.Enabled);
    }
}
