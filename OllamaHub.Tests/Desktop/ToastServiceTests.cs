using OllamaHub.Desktop.Services;
using OllamaHub.Configuration;
using OllamaHub.Desktop.ViewModels;
using Xunit;

namespace OllamaHub.Tests.Desktop;

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
    public void GatewayEndpoint_UsesFullPublicUrl()
    {
        var endpoint = GatewayEndpointEditorViewModel.FromResponse(
            new GatewayEndpointResponse("openai", "OpenAI", "/v1", true, []),
            "http://127.0.0.1:11434/");

        Assert.Equal("http://127.0.0.1:11434/v1", endpoint.PublicUrl);
    }
}
