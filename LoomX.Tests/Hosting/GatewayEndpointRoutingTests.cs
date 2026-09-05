using Microsoft.AspNetCore.Http;
using OllamaHub.Hosting;
using Xunit;

namespace OllamaHub.Tests.Hosting;

public sealed class GatewayEndpointRoutingTests
{
    [Theory]
    [InlineData("/openai/v1/models", "openai")]
    [InlineData("/azure/v1/responses", "azure")]
    [InlineData("/api/chat", "ollama")]
    [InlineData("/v1/chat/completions", "ollama")]
    public void ResolvesCanonicalEndpoint(string path, string endpointKey)
    {
        Assert.Equal(endpointKey, GatewayEndpointRouting.ResolveKey(new PathString(path)));
    }

    [Theory]
    [InlineData("/v1/responses")]
    [InlineData("/unknown")]
    public void RejectsUnownedPaths(string path)
    {
        Assert.Null(GatewayEndpointRouting.ResolveKey(new PathString(path)));
    }
}
