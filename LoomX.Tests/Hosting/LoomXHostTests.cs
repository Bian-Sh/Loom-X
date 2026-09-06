using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using LoomX.Configuration;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Hosting;

public sealed class LoomXHostTests
{
    [Fact]
    public void CreateAsync_迁移发生在首次配置数据库连接之前()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "LoomXHost.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        var migrationIndex = source.IndexOf("EnsureMigratedAsync", StringComparison.Ordinal);
        var configurationContextIndex = source.IndexOf("new ConfigurationDbContext", StringComparison.Ordinal);
        var factoryRegistrationIndex = source.IndexOf("AddDbContextFactory<ConfigurationDbContext>", StringComparison.Ordinal);

        Assert.True(migrationIndex >= 0);
        Assert.True(configurationContextIndex > migrationIndex);
        Assert.True(factoryRegistrationIndex > migrationIndex);
    }

    [Fact]
    public void 根健康响应使用Loomx产品显示名()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "LoomXHost.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        Assert.Contains("name = \"Loom-x\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("name = \"LoomX\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 既有协议路由仍然注册()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", "LoomXHost.cs");
        var source = File.ReadAllText(Path.GetFullPath(path));

        foreach (var route in new[]
        {
            "MapGet(\"/api/tags\"",
            "MapPost(\"/api/chat\"",
            "MapGet(\"/v1/models\"",
            "MapPost(\"/v1/chat/completions\"",
            "MapGet(\"/openai/v1/models\"",
            "MapPost(\"/openai/v1/chat/completions\"",
            "MapGet(\"/azure/v1/models\"",
            "MapPost(\"/azure/v1/responses\""
        })
        {
            Assert.Contains(route, source, StringComparison.Ordinal);
        }
    }

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

        var result = LoomXHost.BuildGatewayAttemptPayload(request, model);

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

        var firstAttempt = LoomXHost.BuildGatewayAttemptPayload(request, first);
        var secondAttempt = LoomXHost.BuildGatewayAttemptPayload(request, second);

        Assert.Equal("first", firstAttempt["model"]!.GetValue<string>());
        Assert.Equal("high", firstAttempt["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("second", secondAttempt["model"]!.GetValue<string>());
        Assert.Equal("low", secondAttempt["reasoning_effort"]!.GetValue<string>());
        Assert.Null(request["reasoning_effort"]);
    }

    [Fact]
    public async Task HandleResponsesAsync_ForwardsModelReasoningEffortToResponsesRelay()
    {
        var model = CreateModel(
            new Dictionary<string, JsonNode?>
            {
                ["reasoning_effort"] = JsonValue.Create("high")
            });
        var comboId = Guid.NewGuid();
        var configuration = new StaticConfigurationProvider(new ResolvedAppConfig
        {
            GatewayCombos =
            [
                new ResolvedGatewayComboConfig
                {
                    Id = comboId,
                    Name = "relay-model",
                    Enabled = true,
                    Routes = [new ResolvedGatewayRouteConfig { Model = model, Enabled = true }]
                }
            ],
            GatewayEndpoints =
            [
                new ResolvedGatewayEndpointConfig
                {
                    Key = "openai",
                    PublicPath = "/openai",
                    Enabled = true,
                    ApiKey = "endpoint-key",
                    ComboBindings = [new ResolvedGatewayComboBindingConfig { ComboId = comboId, Enabled = true }]
                }
            ]
        });
        var passthrough = new CapturingPassthroughClient();
        var context = new DefaultHttpContext();
        context.Request.Path = "/openai/v1/responses";
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers.Authorization = "Bearer endpoint-key";
        context.Response.Body = new MemoryStream();
        var request = JsonNode.Parse("""
        {
          "model": "relay-model",
          "input": [{"role": "user", "content": "hello"}],
          "reasoning_effort": "low"
        }
        """)!.AsObject();

        await LoomXHost.HandleResponsesAsync(
            context,
            configuration,
            passthrough,
            NullLoggerFactory.Instance,
            request,
            CancellationToken.None);

        Assert.NotNull(passthrough.Payload);
        Assert.Equal("deepseek-v4", passthrough.Payload!["model"]!.GetValue<string>());
        Assert.Equal("high", passthrough.Payload["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void BuildGatewayAttemptPayload_UsesOllamaEndpointReasoningDefaultWithoutOverwritingClientValue()
    {
        var model = CreateModel(new Dictionary<string, JsonNode?>());
        var request = JsonNode.Parse("{\"model\":\"public-alias\"}")!.AsObject();

        var defaulted = LoomXHost.BuildGatewayAttemptPayload(request, model, "medium", false);
        Assert.Equal("medium", defaulted["reasoning_effort"]!.GetValue<string>());

        request["reasoning_effort"] = "high";
        var explicitValue = LoomXHost.BuildGatewayAttemptPayload(request, model, "medium", false);
        Assert.Equal("high", explicitValue["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void BuildGatewayAttemptPayload_UsesResponsesReasoningShape()
    {
        var model = CreateModel(new Dictionary<string, JsonNode?>());
        var request = JsonNode.Parse("{\"model\":\"public-alias\"}")!.AsObject();

        var result = LoomXHost.BuildGatewayAttemptPayload(request, model, "low", true);

        Assert.Equal("low", result["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Null(result["reasoning_effort"]);
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

    private sealed class StaticConfigurationProvider(ResolvedAppConfig snapshot) : IDatabaseConfigurationProvider
    {
        public ResolvedAppConfig Current => snapshot;

        public IReadOnlyList<ResolvedModelConfig> GetModels() => snapshot.Models;

        public ResolvedModelConfig? FindModel(string? modelName) => null;

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingPassthroughClient : IProtocolPassthroughClient
    {
        public JsonObject? Payload { get; private set; }

        public Task ProxyAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> ProxyGatewayAttemptAsync<TRequest>(HttpContext httpContext, ResolvedModelConfig model, string apiMode, string upstreamPath, TRequest payload, CancellationToken cancellationToken)
        {
            Payload = payload as JsonObject;
            return Task.FromResult(true);
        }

        public Task<bool> ProxyOpenAiResponsesGatewayAttemptAsync(HttpContext httpContext, ResolvedModelConfig model, JsonObject payload, CancellationToken cancellationToken)
        {
            Payload = payload;
            return Task.FromResult(true);
        }
    }
}
