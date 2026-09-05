using LoomX.Services;
using LoomX.Configuration;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ToastServiceTests
{
    [Fact]
    public void Show_PublishesNotificationWithLevel()
    {
        var service = new ToastService();
        ToastNotification? received = null;
        service.Requested += (_, notification) => received = notification;

        service.Show("地址已复制", ToastLevel.Success);

        Assert.NotNull(received);
        Assert.Equal("地址已复制", received!.Message);
        Assert.Equal(ToastLevel.Success, received.Level);
    }

    [Fact]
    public void Show_IgnoresBlankMessage()
    {
        var service = new ToastService();
        var callCount = 0;
        service.Requested += (_, _) => callCount++;

        service.Show(" ");

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void OllamaGatewayEndpoint_UsesServerRootUrl()
    {
        var endpoint = GatewayEndpointEditorViewModel.FromResponse(
            new GatewayEndpointResponse("ollama", "Ollama", "/api", true, []),
            "http://127.0.0.1:11434/");

        Assert.Equal("http://127.0.0.1:11434", endpoint.PublicUrl);
    }

    [Fact]
    public void OpenAiGatewayEndpoint_RetainsProtocolPath()
    {
        var endpoint = GatewayEndpointEditorViewModel.FromResponse(
            new GatewayEndpointResponse("openai", "OpenAI", "/openai", true, []),
            "http://127.0.0.1:11434/");

        Assert.Equal("http://127.0.0.1:11434/openai", endpoint.PublicUrl);
    }
}
