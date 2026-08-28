using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OllamaHub.Configuration;
using OllamaHub.Contracts;
using OllamaHub.Interop;
using OllamaHub.Services;
using Serilog;

var databasePath = Path.Combine(AppContext.BaseDirectory, "OllamaHub.db");
if (TryHandleCommand(args, databasePath))
{
    Environment.Exit(0);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
var enableConsoleLogging = WindowsConsoleManager.ShouldEnableConsole(LogLevel.Information);

if (enableConsoleLogging)
{
    WindowsConsoleManager.EnsureConsole();
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(Path.Combine(logDirectory, "ollamahub-.log"), rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 30, shared: true)
    .CreateLogger();
builder.Host.UseSerilog();

var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
await using var startupDb = new ConfigurationDbContext(dbOptions);
await ConfigurationDatabase.InitializeAsync(startupDb);
var startupConfiguration = new DatabaseConfigurationProvider(startupDb);
await startupConfiguration.ReloadAsync();
builder.WebHost.UseUrls(startupConfiguration.Current.Server.Urls.ToArray());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddDbContextFactory<ConfigurationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<IDatabaseConfigurationProvider>(startupConfiguration);
builder.Services.AddSingleton<ConfigurationManagementService>();
builder.Services.AddHostedService<ConfigurationRefreshService>();
builder.Services.AddSingleton<IAnthropicRequestFactory, AnthropicRequestFactory>();
builder.Services.AddSingleton<IAnthropicResponseMapper, AnthropicResponseMapper>();
builder.Services.AddHttpClient<IAnthropicProxyClient, AnthropicProxyClient>();
builder.Services.AddHttpClient<IProtocolPassthroughClient, ProtocolPassthroughClient>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "OllamaHub", status = "ok" }));
app.MapGet("/api/version", () => Results.Ok(new { version = "0.12.6" }));
app.MapGet("/api/ps", () => Results.Ok(new { models = Array.Empty<object>() }));

app.MapGet("/api/tags", (IDatabaseConfigurationProvider configProvider) =>
    Results.Ok(new OllamaTagListResponse
    {
        Models = configProvider.GetModels().Select(ToDescriptor).ToArray()
    }));

var adminApi = app.MapGroup("/api/admin");
adminApi.MapGet("/providers", (ConfigurationManagementService service, CancellationToken cancellationToken) => service.ListProvidersAsync(cancellationToken));
adminApi.MapPost("/providers", async (ConfigurationManagementService service, ProviderInput input, CancellationToken cancellationToken) => Results.Ok(await service.CreateProviderAsync(input, cancellationToken)));
adminApi.MapPut("/providers/{id:guid}", async (Guid id, ConfigurationManagementService service, ProviderInput input, CancellationToken cancellationToken) => Results.Ok(await service.UpdateProviderAsync(id, input, cancellationToken)));
adminApi.MapDelete("/providers/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteProviderAsync(id, cancellationToken); return Results.NoContent(); });
adminApi.MapPost("/providers/{providerId:guid}/models", async (Guid providerId, ConfigurationManagementService service, ModelInput input, CancellationToken cancellationToken) => Results.Ok(await service.CreateModelAsync(providerId, input, cancellationToken)));
adminApi.MapPut("/models/{id:guid}", async (Guid id, ConfigurationManagementService service, ModelInput input, CancellationToken cancellationToken) => Results.Ok(await service.UpdateModelAsync(id, input, cancellationToken)));
adminApi.MapDelete("/models/{id:guid}", async (Guid id, ConfigurationManagementService service, CancellationToken cancellationToken) => { await service.DeleteModelAsync(id, cancellationToken); return Results.NoContent(); });

app.MapPost("/api/show", (IDatabaseConfigurationProvider configProvider, OllamaShowRequest request) =>
{
    var modelName = request.Model;
    if (string.IsNullOrWhiteSpace(modelName))
    {
        return Results.BadRequest(new OllamaErrorResponse
        {
            Error = "Model name is required."
        });
    }

    var model = configProvider.FindModel(modelName);
    if (model is null)
    {
        return Results.NotFound(new OllamaErrorResponse
        {
            Error = $"Model '{modelName}' is not configured."
        });
    }

    var capabilities = model.Vision
        ? new[] { "completion", "tools", "vision" }
        : new[] { "completion", "tools" };

    return Results.Ok(new OllamaShowResponse
    {
        Modelfile = $"FROM {model.AnthropicModel}",
        Parameters = $"family={model.Family}\ncontext_length={model.ContextLength}\nmax_tokens={model.MaxTokens}",
        Details = ToDescriptor(model).Details,
        Capabilities = capabilities,
        ModelInfo = new Dictionary<string, object>
        {
            ["provider"] = model.ProviderId,
            ["anthropic_model"] = model.AnthropicModel,
            ["context_length"] = model.ContextLength,
            ["max_tokens"] = model.MaxTokens,
            ["capabilities"] = capabilities,
            ["vision"] = model.Vision,
        }
    });
});


app.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);
app.MapPost("/openai/v1/chat/completions", HandleChatCompletionsAsync);

app.MapFallback((HttpContext httpContext, ILogger<Program> logger) =>
{
    if (HttpMethods.IsPost(httpContext.Request.Method))
    {
        logger.LogError("Unrecognized POST route: {Path}", httpContext.Request.Path.Value);
    }

    return Results.NotFound(new OllamaErrorResponse
    {
        Error = $"Route '{httpContext.Request.Path.Value}' is not recognized."
    });
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var configuredUrls = app.Services.GetRequiredService<IDatabaseConfigurationProvider>().Current.Server.Urls;

    if (configuredUrls.Count > 0)
    {
        logger.LogInformation("OllamaHub listening on configured URLs: {Urls}", string.Join(", ", configuredUrls));
    }
    else
    {
        logger.LogInformation("OllamaHub listening on default ASP.NET Core URLs.");
    }
});

app.Run();

static OllamaModelDescriptor ToDescriptor(ResolvedModelConfig model) =>
    new()
    {
        Name = model.OllamaModelName,
        Model = model.ModelId,
        ModifiedAt = DateTimeOffset.UtcNow.ToString("O"),
        Size = 0,
        Digest = BuildDigest(model),
        Details = new OllamaModelDetails
        {
            Family = "",
            Families = [""],
            ParameterSize = "",
            QuantizationLevel = "proxy"
        }
    };

static string BuildDigest(ResolvedModelConfig model)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{model.ProviderId}:{model.ModelId}:{model.OllamaModelName}"));
    return Convert.ToHexStringLower(bytes);
}

static IResult ToError(HttpStatusCode statusCode, string? error)
{
    var payload = new OllamaErrorResponse
    {
        Error = error ?? "Upstream request failed."
    };

    return (int)statusCode switch
    {
        StatusCodes.Status400BadRequest => Results.BadRequest(payload),
        StatusCodes.Status401Unauthorized => Results.Json(payload, statusCode: StatusCodes.Status401Unauthorized),
        StatusCodes.Status403Forbidden => Results.Json(payload, statusCode: StatusCodes.Status403Forbidden),
        StatusCodes.Status404NotFound => Results.NotFound(payload),
        StatusCodes.Status429TooManyRequests => Results.Json(payload, statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Json(payload, statusCode: StatusCodes.Status502BadGateway)
    };
}

static bool TryGetString(JsonObject jsonObject, string propertyName, out string value)
{
    value = string.Empty;
    if (jsonObject[propertyName] is not JsonValue jsonValue
        || !jsonValue.TryGetValue<string>(out var stringValue)
        || string.IsNullOrWhiteSpace(stringValue))
    {
        return false;
    }

    value = stringValue;
    return true;
}

static bool TryHandleCommand(string[] args, string configPath)
{
    if (args.Length == 0)
    {
        return false;
    }

    if (!string.Equals(args[0], "SetApiKey", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    WindowsConsoleManager.EnsureConsole();

    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: OllamaHub SetApiKey <providerOrModelId> <apiKey>");
        return true;
    }

    try
    {
        var target = args[1];
        var apiKey = args[2];
        SetProtectedApiKey(configPath, target, apiKey);
        Console.WriteLine($"API Key for '{target}' has been stored securely.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
    }

    return true;
}

static void SetProtectedApiKey(string databasePath, string target, string apiKey)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("SetApiKey is only supported on Windows.");
    }

    if (string.IsNullOrWhiteSpace(target))
    {
        throw new ArgumentException("Target provider or model id is required.", nameof(target));
    }

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new ArgumentException("API key is required.", nameof(apiKey));
    }

    var protectedApiKey = ProtectedApiKeyStore.Protect(apiKey);
    var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
    using var db = new ConfigurationDbContext(options);
    ConfigurationDatabase.InitializeAsync(db).GetAwaiter().GetResult();
    var provider = db.Providers.SingleOrDefault(item => item.BusinessId == target);
    if (provider is not null)
    {
        provider.ProtectedApiKey = protectedApiKey;
    }
    else
    {
        var model = db.Models.SingleOrDefault(item => item.ModelId == target);
        if (model is null) throw new InvalidOperationException($"未找到 Provider 或 Model：{target}");
        model.ProtectedApiKey = protectedApiKey;
    }
    db.SaveChanges();
}

async Task<IResult> HandleChatCompletionsAsync(
    HttpContext httpContext,
    IDatabaseConfigurationProvider configProvider,
    IAnthropicRequestFactory requestFactory,
    IAnthropicProxyClient proxyClient,
    IAnthropicResponseMapper responseMapper,
    IProtocolPassthroughClient passthroughClient,
    ILogger<Program> logger,
    JsonNode? requestJson,
    CancellationToken cancellationToken)
{
    if (requestJson is not JsonObject requestObject)
    {
        return Results.BadRequest(new OllamaErrorResponse
        {
            Error = "Request body must be a JSON object."
        });
    }

    if (!TryGetString(requestObject, "model", out var modelName))
    {
        return Results.BadRequest(new OllamaErrorResponse
        {
            Error = "Model name is required."
        });
    }

    var model = configProvider.FindModel(modelName);
    if (model is null)
    {
        logger.LogWarning(
            "OpenAI chat completion model not configured. Requested model: {RequestedModel}. Available models: {AvailableModels}",
            modelName,
            string.Join(", ", configProvider.GetModels().Select(m => m.OllamaModelName)));

        return Results.NotFound(new OllamaErrorResponse
        {
            Error = $"Model '{modelName}' is not configured."
        });
    }

    if (model.SupportsApiMode("openai"))
    {
        requestObject["model"] = model.ModelId;

        if (model.Extra != null && model.Extra.Count > 0)
        {
            foreach (var kvp in model.Extra)
            {
                requestObject[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        if (model.Temperature.HasValue && !requestObject.ContainsKey("temperature"))
            requestObject["temperature"] = model.Temperature.Value;

        if (model.TopP.HasValue && !requestObject.ContainsKey("top_p"))
            requestObject["top_p"] = model.TopP.Value;

        await passthroughClient.ProxyAsync(httpContext, model, "openai", "/v1/chat/completions", requestObject, cancellationToken);
        return Results.Empty;
    }

    var anthropicRequest = requestFactory.Create(model, requestObject);

    if (!anthropicRequest.Stream)
    {
        var (statusCode, response, error) = await proxyClient.SendAsync(model, anthropicRequest, cancellationToken);
        if (response is null)
        {
            return ToError(statusCode, error);
        }

        return Results.Ok(responseMapper.MapOpenAiResponse(model, response));
    }

    var streamResult = await proxyClient.SendStreamAsync(model, anthropicRequest, cancellationToken);
    if (streamResult.Stream is null)
    {
        return ToError(streamResult.StatusCode, streamResult.Error);
    }

    httpContext.Response.StatusCode = StatusCodes.Status200OK;
    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";

    await using var anthropicStream = streamResult.Stream;
    await responseMapper.WriteOpenAiStreamAsync(model, anthropicStream, httpContext.Response.Body, cancellationToken);
    return Results.Empty;
}
