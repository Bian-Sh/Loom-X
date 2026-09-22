using System.Net;
using System.Text;
using LoomX.Configuration;
using LoomX.Contracts;
using LoomX.Services;
using LoomX.Tests.Logging;
using Xunit;

namespace LoomX.Tests.Services;

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

    [Fact]
    public async Task BaseUrl已包含V1时真实路由保留重复版本段并记录警告()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, "{\"content\":[]}");
        using var httpClient = new HttpClient(handler);
        var logger = new RecordingLogger<AnthropicProxyClient>();
        var client = new AnthropicProxyClient(httpClient, logger);
        var model = new ResolvedModelConfig
        {
            ModelId = "claude-sonnet-4-5",
            OllamaModelName = "claude-sonnet-4-5",
            DisplayName = "Claude Sonnet",
            ProviderId = "anthropic",
            ApiModes = ["anthropic"],
            BaseUrl = "https://api.anthropic.com/v1",
            ApiKey = "secret",
            AnthropicModel = "claude-sonnet-4-5"
        };

        await client.SendAsync(model, new AnthropicMessagesRequest
        {
            Model = model.ModelId,
            Messages =
            [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = [new AnthropicContentBlock { Type = "text", Text = "hello" }]
                }
            ]
        }, CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/v1/v1/messages", handler.RequestUri);
        Assert.Contains(logger.Messages, message => message.Contains("重复版本段", StringComparison.Ordinal)
            && message.Contains("/v1/v1/messages", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret", StringComparison.Ordinal));
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
