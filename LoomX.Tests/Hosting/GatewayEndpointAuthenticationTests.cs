using Microsoft.AspNetCore.Http;
using LoomX.Configuration;
using LoomX.Hosting;
using Xunit;

namespace LoomX.Tests.Hosting;

public sealed class GatewayEndpointAuthenticationTests
{
    [Fact]
    public void OpenAiRequiresBearerKey()
    {
        var context = new DefaultHttpContext();
        var endpoint = new ResolvedGatewayEndpointConfig { Key = "openai", PublicPath = "/openai", ApiKey = "gateway-key" };

        Assert.False(GatewayEndpointAuthentication.IsAuthorized(context, endpoint));
        context.Request.Headers.Authorization = "Bearer gateway-key";
        Assert.True(GatewayEndpointAuthentication.IsAuthorized(context, endpoint));
        context.Request.Headers.Authorization = "Bearer wrong-key";
        Assert.False(GatewayEndpointAuthentication.IsAuthorized(context, endpoint));
    }

    [Fact]
    public void AzureAcceptsApiKeyHeader()
    {
        var context = new DefaultHttpContext();
        var endpoint = new ResolvedGatewayEndpointConfig { Key = "azure", PublicPath = "/azure", ApiKey = "gateway-key" };

        context.Request.Headers["api-key"] = "gateway-key";

        Assert.True(GatewayEndpointAuthentication.IsAuthorized(context, endpoint));
    }

    [Fact]
    public void OllamaDoesNotRequireKey()
    {
        var context = new DefaultHttpContext();
        var endpoint = new ResolvedGatewayEndpointConfig { Key = "ollama", PublicPath = "/" };

        Assert.True(GatewayEndpointAuthentication.IsAuthorized(context, endpoint));
    }
}
