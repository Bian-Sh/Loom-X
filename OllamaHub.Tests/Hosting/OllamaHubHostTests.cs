using System.Text.Json.Nodes;
using OllamaHub.Configuration;
using Xunit;

namespace OllamaHub.Tests.Hosting;

public sealed class OllamaHubHostTests
{
    [Fact]
    public void BuildGatewayAttemptPayload_ForwardsModelReasoningEffortWithoutMutatingRequest()
    {
        var request = JsonNode.Parse("""
        {
          "model": "public-alias",
          "messages": [{"role": "user", "content": "hello"}],
          "reasoning_effort": "low"
        }
        """)!.AsObject();
        var model = CreateModel(
            new Dictionary<string, JsonNode?>
            {
                ["reasoning_effort"] = JsonValue.Create("high"),
                ["provider_options"] = new JsonObject { ["enabled"] = true }
            });

        var result = OllamaHubHost.BuildGatewayAttemptPayload(request, model);

        Assert.Equal("deepseek-v4", result["model"]!.GetValue<string>());
        Assert.Equal("high", result["reasoning_effort"]!.GetValue<string>());
        Assert.True(result["provider_options"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("public-alias", request["model"]!.GetValue<string>());
        Assert.Equal("low", request["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void BuildGatewayAttemptPayload_ClonesRequestForEachRoute()
    {
        var request = JsonNode.Parse("""
        {
          "model": "public-alias",
          "messages": [{"role": "user", "content": "hello"}]
        }
        """)!.AsObject();
        var first = BuildModel("first", "high");
        var second = BuildModel("second", "low");

        var firstAttempt = OllamaHubHost.BuildGatewayAttemptPayload(request, first);
        var secondAttempt = OllamaHubHost.BuildGatewayAttemptPayload(request, second);

        Assert.Equal("first", firstAttempt["model"]!.GetValue<string>());
        Assert.Equal("high", firstAttempt["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("second", secondAttempt["model"]!.GetValue<string>());
        Assert.Equal("low", secondAttempt["reasoning_effort"]!.GetValue<string>());
        Assert.Null(request["reasoning_effort"]);
    }

    private static ResolvedModelConfig CreateModel(IReadOnlyDictionary<string, JsonNode?> extra) => new()
    {
        ModelId = "deepseek-v4",
        OllamaModelName = "deepseek-v4",
        DisplayName = "DeepSeek V4",
        ProviderId = "relay",
        ApiModes = ["openai"],
        BaseUrl = "https://relay.example.com/v1",
        ApiKey = "secret",
        AnthropicModel = "deepseek-v4",
        Extra = extra
    };

    private static ResolvedModelConfig BuildModel(string modelId, string reasoningEffort) => new()
    {
        ModelId = modelId,
        OllamaModelName = modelId,
        DisplayName = modelId,
        ProviderId = "relay",
        ApiModes = ["openai"],
        BaseUrl = "https://relay.example.com/v1",
        ApiKey = "secret",
        AnthropicModel = modelId,
        Extra = new Dictionary<string, JsonNode?>
        {
            ["reasoning_effort"] = JsonValue.Create(reasoningEffort)
        }
    };
}
