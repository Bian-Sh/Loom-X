using System.Net;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LoomX.Configuration;
using LoomX.Contracts;
using LoomX.Services;
using LoomX.Activity;
using LoomX.Logging;
using LoomX.Hosting;
using Serilog;

namespace LoomX;

public static class LoomXHost
{
    public static async Task<WebApplication> CreateAsync(CancellationToken cancellationToken = default)
    {
        AppDataPaths.EnsureCreated();

        LoggingBootstrap.Configure();
        using (var migrationLoggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: false)))
        {
            var migration = new ApplicationDataMigration(
                migrationLoggerFactory.CreateLogger<ApplicationDataMigration>());
            await migration.EnsureMigratedAsync(cancellationToken);
        }

        var databasePath = AppDataPaths.DatabasePath;
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Host.UseSerilog();

        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite(CreateConnectionString(databasePath)).Options;
        var startupDb = new ConfigurationDbContext(dbOptions);
        await ConfigurationDatabase.InitializeAsync(startupDb, cancellationToken);
        var startupConfiguration = new DatabaseConfigurationProvider(startupDb);
        await startupConfiguration.ReloadAsync(cancellationToken);
        LoggingBootstrap.SetIncludeStackTrace(startupConfiguration.Current.Settings.LogStackTrace);
        builder.WebHost.UseUrls(startupConfiguration.Current.Server.Urls.ToArray());

        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = null);
        builder.Services.AddDbContextFactory<ConfigurationDbContext>(options => options.UseSqlite(CreateConnectionString(databasePath)));
        builder.Services.AddSingleton<IDatabaseConfigurationProvider>(startupConfiguration);
        builder.Services.AddSingleton<ConfigurationManagementService>();
        builder.Services.AddSingleton<IAnthropicRequestFactory, AnthropicRequestFactory>();
        builder.Services.AddSingleton<IAnthropicResponseMapper, AnthropicResponseMapper>();
        builder.Services.AddHttpClient<IAnthropicProxyClient, AnthropicProxyClient>();
        builder.Services.AddHttpClient<IProtocolPassthroughClient, ProtocolPassthroughClient>();
        builder.Services.AddSingleton<ActivityStore>();
        builder.Services.AddSingleton<IActivityStore>(services => services.GetRequiredService<ActivityStore>());
        builder.Services.AddSingleton<RequestTelemetryHub>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ActivityStore>());

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(startupDb.Dispose);
        app.UseMiddleware<ActivityMiddleware>();
        MapEndpoints(app);
        return app;
    }

    private static string CreateConnectionString(string databasePath) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Cache = SqliteCacheMode.Private,
        Pooling = false,
        DefaultTimeout = 5
    }.ToString();

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new { name = "Loom-x", status = "ok" }));
        app.MapGet("/api/version", () => Results.Ok(new { version = AppVersion.Current }));
        app.MapGet("/api/ps", () => Results.Ok(new { models = Array.Empty<object>() }));
        app.MapGet("/api/tags", (IDatabaseConfigurationProvider configProvider) =>
            Results.Ok(new OllamaTagListResponse { Models = ListEnabledCombos(configProvider, "ollama").Select(ToDescriptor).ToArray() }));

        var adminApi = app.MapGroup("/api/admin");
        adminApi.MapGet("/settings", (ConfigurationManagementService service, CancellationToken cancellationToken) => service.GetSettingsAsync(cancellationToken));
        adminApi.MapPut("/settings", async (ConfigurationManagementService service, AppSettingsInput input, CancellationToken cancellationToken) => Results.Ok(await service.UpdateSettingsAsync(input, cancellationToken)));
        adminApi.MapGet("/providers", (ConfigurationManagementService service, CancellationToken cancellationToken) => service.ListProvidersAsync(cancellationToken));
        adminApi.MapPost("/providers", async (ConfigurationManagementService service, ProviderInput input, CancellationToken cancellationToken) => Results.Ok(await service.CreateProviderAsync(input, cancellationToken)));
        adminApi.MapPut("/providers/{id:guid}", async (Guid id, ConfigurationManagementService service, ProviderInput input, CancellationToken cancellationToken) => Results.Ok(await service.UpdateProviderAsync(id, input, cancellationToken)));
        adminApi.MapDelete("/providers/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteProviderAsync(id, cancellationToken); return Results.NoContent(); });
        adminApi.MapPost("/providers/{providerId:guid}/models", async (Guid providerId, ConfigurationManagementService service, ModelInput input, CancellationToken cancellationToken) => Results.Ok(await service.CreateModelAsync(providerId, input, cancellationToken)));
        adminApi.MapPut("/models/{id:guid}", async (Guid id, ConfigurationManagementService service, ModelInput input, CancellationToken cancellationToken) => Results.Ok(await service.UpdateModelAsync(id, input, cancellationToken)));
        adminApi.MapDelete("/models/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteModelAsync(id, cancellationToken); return Results.NoContent(); });
        adminApi.MapGet("/gateway", (ConfigurationManagementService service, CancellationToken cancellationToken) => service.ListGatewayEndpointsAsync(cancellationToken));
        adminApi.MapPut("/gateway/{key}/enabled", async (string key, GatewayEndpointToggleInput input, ConfigurationManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.SetGatewayEndpointEnabledAsync(key, input.Enabled, cancellationToken)));
        adminApi.MapPost("/gateway/{key}/combos", async (string key, GatewayComboInput input, ConfigurationManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateGatewayComboAsync(key, input, cancellationToken)));
        adminApi.MapPut("/gateway/combos/{id:guid}", async (Guid id, GatewayComboInput input, ConfigurationManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.UpdateGatewayComboAsync(id, input, cancellationToken)));
        adminApi.MapDelete("/gateway/combos/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteGatewayComboAsync(id, cancellationToken); return Results.NoContent(); });
        adminApi.MapPost("/gateway/combos/{id:guid}/routes", async (Guid id, GatewayRouteInput input, ConfigurationManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.CreateGatewayRouteAsync(id, input, cancellationToken)));
        adminApi.MapPut("/gateway/routes/{id:guid}", async (Guid id, GatewayRouteInput input, ConfigurationManagementService service, CancellationToken cancellationToken) => Results.Ok(await service.UpdateGatewayRouteAsync(id, input, cancellationToken)));
        adminApi.MapDelete("/gateway/routes/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteGatewayRouteAsync(id, cancellationToken); return Results.NoContent(); });

        app.MapPost("/api/show", (IDatabaseConfigurationProvider configProvider, OllamaShowRequest request) =>
        {
            var modelName = request.Model;
            if (string.IsNullOrWhiteSpace(modelName)) return Results.BadRequest(new OllamaErrorResponse { Error = "Model name is required." });
            var combo = FindEnabledCombo(configProvider, "ollama", modelName);
            var model = combo?.Routes.FirstOrDefault(item => item.Enabled)?.Model;
            if (model is null) return Results.NotFound(new OllamaErrorResponse { Error = $"Combo model '{modelName}' is not configured for this Endpoint." });
            var capabilities = model.Vision ? new[] { "completion", "tools", "vision" } : new[] { "completion", "tools" };
            return Results.Ok(new OllamaShowResponse
            {
                Modelfile = $"FROM {combo!.Name}",
                Parameters = $"family={model.Family}\ncontext_length={model.ContextLength}\nmax_tokens={model.MaxTokens}",
                Details = ToDescriptor(combo!).Details,
                Capabilities = capabilities,
                ModelInfo = new Dictionary<string, object>
                {
                    ["context_length"] = model.ContextLength,
                    ["max_tokens"] = model.MaxTokens,
                    ["capabilities"] = capabilities,
                    ["vision"] = model.Vision,
                }
            });
        });

        app.MapPost("/openai/v1/responses", HandleResponsesAsync);
        app.MapPost("/azure/v1/responses", HandleResponsesAsync);
        app.MapPost("/api/chat", HandleResponsesAsync);
        app.MapGet("/v1/models", (IDatabaseConfigurationProvider provider) => Results.Ok(ListGatewayModels(provider, "ollama")));
        app.MapGet("/openai/v1/models", (IDatabaseConfigurationProvider provider) => Results.Ok(ListGatewayModels(provider, "openai")));
        app.MapGet("/azure/v1/models", (IDatabaseConfigurationProvider provider) => Results.Ok(ListGatewayModels(provider, "azure")));
        app.MapPost("/v1/chat/completions", HandleResponsesAsync);
        app.MapPost("/openai/v1/chat/completions", HandleChatCompletionsAsync);
        app.MapFallback((HttpContext httpContext, ILoggerFactory loggerFactory) =>
        {
            if (HttpMethods.IsPost(httpContext.Request.Method)) loggerFactory.CreateLogger("Fallback").LogError("Unrecognized POST route: {Path}", httpContext.Request.Path.Value);
            return Results.NotFound(new OllamaErrorResponse { Error = $"Route '{httpContext.Request.Path.Value}' is not recognized." });
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
            var configuredUrls = app.Services.GetRequiredService<IDatabaseConfigurationProvider>().Current.Server.Urls;
            logger.LogInformation("Loom-x 网关监听 {Urls}", configuredUrls.Count > 0 ? string.Join(", ", configuredUrls) : "默认 ASP.NET Core 地址");
        });
    }

    private static OllamaModelDescriptor ToDescriptor(ResolvedGatewayComboConfig combo) => new()
    {
        Name = combo.Name,
        Model = combo.Name,
        ModifiedAt = DateTimeOffset.UtcNow.ToString("O"),
        Size = 0,
        Digest = BuildDigest(combo.Name),
        Details = new OllamaModelDetails { Family = "", Families = [""], ParameterSize = "", QuantizationLevel = "proxy" }
    };

    private static string BuildDigest(string comboName)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(comboName));
        return Convert.ToHexStringLower(bytes);
    }

    private static IResult ToError(HttpStatusCode statusCode, string? error) => Results.Json(new OllamaErrorResponse { Error = error ?? "Upstream request failed." }, statusCode: statusCode switch
    {
        HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
        HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
        HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
        HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
        (HttpStatusCode)429 => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status502BadGateway
    });

    private static bool TryGetString(JsonObject jsonObject, string propertyName, out string value)
    {
        value = string.Empty;
        if (jsonObject[propertyName] is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var stringValue) || string.IsNullOrWhiteSpace(stringValue)) return false;
        value = stringValue;
        return true;
    }

    private static async Task<IResult> HandleChatCompletionsAsync(
        HttpContext httpContext,
        IDatabaseConfigurationProvider configProvider,
        IAnthropicRequestFactory requestFactory,
        IAnthropicProxyClient proxyClient,
        IAnthropicResponseMapper responseMapper,
        IProtocolPassthroughClient passthroughClient,
        RequestTelemetryHub telemetryHub,
        ILoggerFactory loggerFactory,
        JsonNode? requestJson,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChatCompletions");
        if (requestJson is not JsonObject requestObject) return Results.BadRequest(new OllamaErrorResponse { Error = "Request body must be a JSON object." });
        var requestedModel = TryGetString(requestObject, "model", out var modelName) ? modelName : null;
        var combo = requestedModel is null
            ? ListEnabledCombos(configProvider, "openai").FirstOrDefault()
            : FindEnabledCombo(configProvider, "openai", requestedModel);
        var routes = combo?.Routes.Where(item => item.Enabled).OrderBy(item => item.SortOrder).ToArray() ?? [];
        if (httpContext.Items[ActivityContextKeys.Request] is ActivityRequestContext activityContext)
        {
            activityContext.ModelAlias = combo?.Name ?? requestedModel;
            activityContext.Route = "OpenAI Combo";
            activityContext.IsStreaming = requestObject["stream"] is JsonValue streamValue
                && streamValue.TryGetValue<bool>(out var isStreaming)
                && isStreaming;
        }
        if (routes.Length == 0)
        {
            logger.LogWarning("OpenAI chat completion Combo model not configured. Requested model: {RequestedModel}", requestedModel ?? "默认");
            return Results.NotFound(new OllamaErrorResponse { Error = requestedModel is null ? "No enabled model route is configured." : $"Combo model '{requestedModel}' is not configured for this Endpoint." });
        }

        foreach (var route in routes)
        {
            var model = route.Model;
            if (httpContext.Items[ActivityContextKeys.Request] is ActivityRequestContext routeContext)
            {
                routeContext.AttemptIndex++;
                routeContext.ProviderId = model.ProviderId;
                routeContext.ModelId = model.ModelId;
                routeContext.ModelAlias = combo!.Name;
                routeContext.Route = model.SupportsApiMode("openai") ? "OpenAI Combo" : "OpenAI Combo → Anthropic";
            }

            var attemptRequest = BuildGatewayAttemptPayload(requestObject, model);
            if (model.SupportsApiMode("openai"))
            {
                if (model.Temperature.HasValue && !attemptRequest.ContainsKey("temperature")) attemptRequest["temperature"] = model.Temperature.Value;
                if (model.TopP.HasValue && !attemptRequest.ContainsKey("top_p")) attemptRequest["top_p"] = model.TopP.Value;
                if (await passthroughClient.ProxyGatewayAttemptAsync(httpContext, model, "openai", "/chat/completions", attemptRequest, cancellationToken)) return Results.Empty;
                continue;
            }

            var anthropicRequest = requestFactory.Create(model, attemptRequest);
            var telemetryContext = httpContext.Items[ActivityContextKeys.Request] as ActivityRequestContext;
            var attemptStartedAt = Stopwatch.GetTimestamp();
            if (telemetryContext is not null) telemetryHub.EdgeAttemptStarted(telemetryContext, model.ProviderId, model.ModelId, telemetryContext.AttemptIndex);
            if (!anthropicRequest.Stream)
            {
                var (statusCode, response, error) = await proxyClient.SendAsync(model, anthropicRequest, cancellationToken);
                var elapsedMs = (long)Stopwatch.GetElapsedTime(attemptStartedAt).TotalMilliseconds;
                if (response is not null)
                {
                    if (telemetryContext is not null) telemetryHub.EdgeAttemptCompleted(telemetryContext, model.ProviderId, model.ModelId, (int)statusCode, elapsedMs);
                    return Results.Ok(responseMapper.MapOpenAiResponse(model, response));
                }
                var retryable = (int)statusCode is 408 or 429 or >= 500;
                if (telemetryContext is not null) telemetryHub.EdgeAttemptFailed(telemetryContext, model.ProviderId, model.ModelId, (int)statusCode, elapsedMs, retryable);
                if (retryable) continue;
                return ToError(statusCode, error);
            }

            var streamResult = await proxyClient.SendStreamAsync(model, anthropicRequest, cancellationToken);
            var streamElapsedMs = (long)Stopwatch.GetElapsedTime(attemptStartedAt).TotalMilliseconds;
            if (streamResult.Stream is null)
            {
                var retryable = (int)streamResult.StatusCode is 408 or 429 or >= 500;
                if (telemetryContext is not null) telemetryHub.EdgeAttemptFailed(telemetryContext, model.ProviderId, model.ModelId, (int)streamResult.StatusCode, streamElapsedMs, retryable);
                if (retryable) continue;
                return ToError(streamResult.StatusCode, streamResult.Error);
            }
            if (telemetryContext is not null) telemetryHub.EdgeAttemptCompleted(telemetryContext, model.ProviderId, model.ModelId, (int)streamResult.StatusCode, streamElapsedMs);
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            await using var anthropicStream = streamResult.Stream;
            await responseMapper.WriteOpenAiStreamAsync(model, anthropicStream, httpContext.Response.Body, cancellationToken);
            return Results.Empty;
        }
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    internal static async Task<IResult> HandleResponsesAsync(
        HttpContext httpContext,
        IDatabaseConfigurationProvider configProvider,
        IProtocolPassthroughClient passthroughClient,
        ILoggerFactory loggerFactory,
        JsonNode? requestJson,
        CancellationToken cancellationToken)
    {
        var endpointKey = GatewayEndpointRouting.ResolveKey(httpContext.Request.Path);
        if (endpointKey is null) return Results.NotFound(new OllamaErrorResponse { Error = "Endpoint is not configured for this URI." });
        var endpoint = configProvider.Current.GatewayEndpoints.FirstOrDefault(item => item.Key == endpointKey && item.Enabled);
        if (endpoint is null) return Results.NotFound(new OllamaErrorResponse { Error = "Endpoint is disabled or not configured." });
        if (requestJson is not JsonObject requestObject) return Results.BadRequest(new OllamaErrorResponse { Error = "Request body must be a JSON object." });
        var requestedModel = TryGetString(requestObject, "model", out var modelName) ? modelName : null;
        var combo = requestedModel is null
            ? ListEnabledCombos(configProvider, endpointKey).FirstOrDefault()
            : FindEnabledCombo(configProvider, endpointKey, requestedModel);
        var routes = combo?.Routes.Where(item => item.Enabled).OrderBy(item => item.SortOrder).ToArray() ?? [];
        if (routes.Length == 0) return Results.NotFound(new OllamaErrorResponse { Error = requestedModel is null ? "No enabled model route is configured." : $"Model '{requestedModel}' is not configured for this Endpoint." });
        if (httpContext.Items[ActivityContextKeys.Request] is ActivityRequestContext activityContext)
        {
            activityContext.ModelAlias = combo?.Name;
            activityContext.Protocol = GatewayEndpointRouting.ResolveLabel(httpContext.Request.Path);
        }
        for (var routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            var route = routes[routeIndex];
            if (httpContext.Items[ActivityContextKeys.Request] is ActivityRequestContext requestContext) requestContext.AttemptIndex++;
            if (httpContext.Items[ActivityContextKeys.Request] is ActivityRequestContext routeContext)
            {
                routeContext.ProviderId = route.Model.ProviderId;
                routeContext.ModelId = route.Model.ModelId;
                routeContext.ModelAlias = combo!.Name;
                routeContext.Route = $"{GatewayEndpointRouting.ResolveLabel(httpContext.Request.Path)} → {route.Model.ProviderId}";
            }
            var attemptRequest = BuildGatewayAttemptPayload(requestObject, route.Model);
            if (endpointKey == "ollama"
                && httpContext.Request.Path.StartsWithSegments("/v1/chat/completions")
                && route.Model.SupportsApiMode("openai")
                && !route.Model.EndpointFormat.Equals("chat_completions", StringComparison.OrdinalIgnoreCase))
            {
                if (await passthroughClient.ProxyOpenAiResponsesGatewayAttemptAsync(httpContext, route.Model, attemptRequest, cancellationToken)) return Results.Empty;
                continue;
            }

            var upstreamPath = route.Model.EndpointFormat.Equals("chat_completions", StringComparison.OrdinalIgnoreCase) ? "/chat/completions" : "/responses";
            if (route.Model.SupportsApiMode("ollama")) upstreamPath = "/api/chat";
            if (await passthroughClient.ProxyGatewayAttemptAsync(httpContext, route.Model, route.Model.SupportsApiMode("ollama") ? "ollama" : "openai", upstreamPath, attemptRequest, cancellationToken)) return Results.Empty;
        }
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    internal static JsonObject BuildGatewayAttemptPayload(JsonObject requestObject, ResolvedModelConfig model)
    {
        var attemptRequest = requestObject.DeepClone().AsObject();
        attemptRequest["model"] = model.ModelId;
        foreach (var kvp in model.Extra)
        {
            attemptRequest[kvp.Key] = kvp.Value?.DeepClone();
        }

        return attemptRequest;
    }

    private static object ListGatewayModels(IDatabaseConfigurationProvider provider, string endpointKey)
    {
        var data = ListEnabledCombos(provider, endpointKey).Select(item => new { id = item.Name, @object = "model", owned_by = "loomx" }).ToArray();
        return new { @object = "list", data };
    }

    private static IReadOnlyList<ResolvedGatewayComboConfig> ListEnabledCombos(IDatabaseConfigurationProvider provider, string endpointKey) =>
        GatewayComboCatalog.ForEndpoint(provider.Current, endpointKey);

    private static ResolvedGatewayComboConfig? FindEnabledCombo(IDatabaseConfigurationProvider provider, string endpointKey, string name) =>
        ListEnabledCombos(provider, endpointKey).FirstOrDefault(item => GatewayComboMatcher.Matches(item, name));
}
