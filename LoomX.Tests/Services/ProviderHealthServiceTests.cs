using System.Net;
using System.Net.Http.Headers;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class ProviderHealthServiceTests
{
    [Fact]
    public async Task SuccessResponseReportsHealthyAndSendsAuthenticationAndHeaders()
    {
        HttpRequestMessage? captured = null;
        using var handler = new StubHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, "{\"data\":[{\"id\":\"m1\"},{\"id\":\"m2\"}]}");
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        var service = new ProviderHealthService(client);

        var result = await service.CheckAsync(new(
            "provider",
            true,
            "https://example.com/v1",
            null,
            "secret",
            new Dictionary<string, string> { ["X-Test"] = "ready" },
            0,
            false));

        Assert.Equal(ProviderHealthState.Healthy, result.State);
        Assert.Equal(2, result.DiscoveredModelCount);
        Assert.Equal("https://example.com/v1/models", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("secret", captured.Headers.Authorization.Parameter);
        Assert.Equal("ready", captured.Headers.GetValues("X-Test").Single());
    }

    [Fact]
    public async Task EmptyModelsReportsWarningStateWithoutFailure()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{\"models\":[]}"));
        using var client = new HttpClient(handler);
        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest());

        Assert.Equal(ProviderHealthState.HealthyEmptyModels, result.State);
        Assert.Equal(0, result.DiscoveredModelCount);
        Assert.Equal(ProviderHealthFailureKind.None, result.FailureKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderHealthState.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, ProviderHealthState.AuthFailed)]
    [InlineData(HttpStatusCode.NotFound, ProviderHealthState.EndpointError)]
    [InlineData(HttpStatusCode.MethodNotAllowed, ProviderHealthState.EndpointError)]
    [InlineData((HttpStatusCode)429, ProviderHealthState.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ProviderHealthState.RequestRejected)]
    [InlineData(HttpStatusCode.BadGateway, ProviderHealthState.UpstreamError)]
    public async Task HttpStatusIsMappedToUsefulState(HttpStatusCode statusCode, ProviderHealthState expected)
    {
        using var handler = new StubHandler(_ => Json(statusCode, "{}"));
        using var client = new HttpClient(handler);

        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest());

        Assert.Equal(expected, result.State);
        Assert.Equal((int)statusCode, result.StatusCode);
    }

    [Fact]
    public async Task InvalidJsonReportsProtocolError()
    {
        using var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        using var client = new HttpClient(handler);

        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest());

        Assert.Equal(ProviderHealthState.ProtocolError, result.State);
        Assert.Equal(ProviderHealthFailureKind.Protocol, result.FailureKind);
    }

    [Fact]
    public async Task TimeoutReportsUnavailable()
    {
        using var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };

        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest());

        Assert.Equal(ProviderHealthState.Unavailable, result.State);
        Assert.Equal("timeout", result.FailureCode);
    }

    [Fact]
    public async Task InvalidConfigurationDoesNotSendRequest()
    {
        var called = false;
        using var handler = new StubHandler(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, "{\"data\":[]}");
        });
        using var client = new HttpClient(handler);

        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest() with { BaseUrl = "ftp://example.com" });

        Assert.Equal(ProviderHealthState.ConfigurationError, result.State);
        Assert.Equal("invalid_url", result.FailureCode);
        Assert.False(called);
    }

    [Fact]
    public async Task IncompleteHeadersDoNotSendRequest()
    {
        var called = false;
        using var handler = new StubHandler(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, "{\"data\":[]}");
        });
        using var client = new HttpClient(handler);

        var result = await new ProviderHealthService(client).CheckAsync(CreateRequest() with { IncompleteHeaderCount = 1 });

        Assert.Equal(ProviderHealthState.ConfigurationError, result.State);
        Assert.Equal("incomplete_headers", result.FailureCode);
        Assert.False(called);
    }

    private static ProviderHealthCheckRequest CreateRequest() => new(
        "provider",
        true,
        "https://example.com",
        null,
        null,
        new Dictionary<string, string>(),
        0,
        false);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
