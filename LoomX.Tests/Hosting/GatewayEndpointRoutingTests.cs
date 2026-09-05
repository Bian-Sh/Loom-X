using Microsoft.AspNetCore.Http;
using LoomX.Hosting;
using Xunit;

namespace LoomX.Tests.Hosting;

public sealed class GatewayEndpointRoutingTests
{
    [Theory]
    [InlineData("/api/version", "ollama")]
    [InlineData("/api/ps", "ollama")]
    [InlineData("/api/tags", "ollama")]
    [InlineData("/api/show", "ollama")]
    [InlineData("/openai/v1/models", "openai")]
    [InlineData("/openai/v1/responses", "openai")]
    [InlineData("/openai/v1/chat/completions", "openai")]
    [InlineData("/azure/v1/responses", "azure")]
    [InlineData("/azure/v1/models", "azure")]
    [InlineData("/api/chat", "ollama")]
    [InlineData("/v1/models", "ollama")]
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
