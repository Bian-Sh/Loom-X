using System.Net;
using System.Text;
using OllamaHub.Configuration;
using OllamaHub.Contracts;
using OllamaHub.Services;
using OllamaHub.Tests.Logging;
using Xunit;

namespace OllamaHub.Tests.Services;

public sealed class AnthropicProxyClientTests
{
    [Fact]
    public async Task SendAsync_FailureLog_ContainsSafeSummaryWithoutBodies()
    {
        const string prompt = "sensitive-user-prompt";
        const string upstreamBody = "{\"error\":{\"message\":\"sensitive-upstream-response\"}}";
        using var httpClient = new HttpClient(new ResponseHandler(HttpStatusCode.BadRequest, upstreamBody));
        var logger = new RecordingLogger<AnthropicProxyClient>();
        var client = new AnthropicProxyClient(httpClient, logger);
        var model = new ResolvedModelConfig
        {
            ModelId = "claude-sonnet-4-5",
            OllamaModelName = "claude-sonnet-4-5",
            DisplayName = "Claude Sonnet",
            ProviderId = "anthropic",
            ApiModes = ["anthropic"],
            BaseUrl = "https://api.anthropic.com",
            ApiKey = "secret",
            AnthropicModel = "claude-sonnet-4-5"
        };
        var request = new AnthropicMessagesRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = [new AnthropicContentBlock { Type = "text", Text = prompt }]
                }
            ]
        };

        await client.SendAsync(model, request, CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains(model.ProviderId, message);
        Assert.Contains(model.ModelId, message);
        Assert.Contains("400", message);
        Assert.DoesNotContain(prompt, message);
        Assert.DoesNotContain("sensitive-upstream-response", message);
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
