using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Services;

public enum ProviderTestMode
{
    Regular,
    Streaming,
}

public enum ProviderTestStatus
{
    Preparing,
    Sending,
    Completed,
    Failed,
    Cancelled,
}

public sealed record ProviderTestRequest(
    string RequestId,
    string ProviderId,
    string ModelId,
    string BaseUrl,
    string ApiMode,
    string EndpointFormat,
    string? ApiKey,
    IReadOnlyDictionary<string, string> Headers,
    bool UseProxy,
    string Prompt,
    ProviderTestMode Mode,
    int MaxDisplayCharacters);

public sealed record ProviderTestProgress(
    string RequestId,
    ProviderTestStatus Status,
    string TextDelta = "",
    int AccumulatedCharacters = 0,
    bool IsTruncated = false,
    int? StatusCode = null,
    string? ContentType = null,
    long? ResponseBytes = null);

public sealed record ProviderTestSummary(
    string RequestId,
    string ProviderId,
    string ModelId,
    string Protocol,
    string Path,
    ProviderTestMode Mode,
    bool UseProxy,
    string ProxySummary,
    string? CliSummary,
    int CustomHeaderCount);

public sealed record ProviderTestResult(
    string RequestId,
    ProviderTestStatus Status,
    ProviderTestSummary Summary,
    int? StatusCode = null,
    string? ContentType = null,
    long ElapsedMs = 0,
    long ResponseBytes = 0,
    string ResponseText = "",
    bool IsTruncated = false,
    string? ErrorCode = null,
    bool CanRetry = false)
{
    public bool IsSuccess => Status == ProviderTestStatus.Completed;
    public bool IsCancelled => Status == ProviderTestStatus.Cancelled;
}

public interface IProviderTestService
{
    Task<ProviderTestResult> ExecuteAsync(
        ProviderTestRequest request,
        IProgress<ProviderTestProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderTestService : IProviderTestService
{
    private const string AnthropicVersion = "2023-06-01";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly ILogger<ProviderTestService> logger;
    private readonly IProviderExecutionPipeline executionPipeline;

    public ProviderTestService(
        HttpClient httpClient,
        ILogger<ProviderTestService>? logger = null,
        IProviderExecutionPipeline? executionPipeline = null)
    {
        this.httpClient = httpClient;
        this.logger = logger ?? NullLogger<ProviderTestService>.Instance;
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
    }

    public async Task<ProviderTestResult> ExecuteAsync(
        ProviderTestRequest request,
        IProgress<ProviderTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var protocol = ResolveProtocol(request);
        var path = ResolvePath(protocol);
        var summary = new ProviderTestSummary(
            request.RequestId,
            request.ProviderId,
            request.ModelId,
            protocol,
            path,
            request.Mode,
            request.UseProxy,
            request.UseProxy ? "enabled" : "direct",
            null,
            request.Headers.Count);

        logger.LogInformation(
            "Provider 测试开始 {ProviderId}/{ModelId} {Protocol} {Path} {Mode} {UseProxy}",
            request.ProviderId,
            request.ModelId,
            protocol,
            path,
            request.Mode,
            request.UseProxy);

        var startedAt = Stopwatch.GetTimestamp();
        progress?.Report(new ProviderTestProgress(request.RequestId, ProviderTestStatus.Sending));

        try
        {
            using var httpRequest = BuildRequest(request, protocol, path);
            var executionResult = await executionPipeline.ExecuteAsync(
                httpClient,
                httpRequest,
                new ProviderExecutionContext(request.ProviderId, request.ModelId, request.ApiMode, path),
                cancellationToken);

            if (!executionResult.IsSuccess)
            {
                var failure = MapHttpFailure(executionResult.StatusCode);
                return CreateFailureResult(
                    request,
                    summary,
                    progress,
                    startedAt,
                    failure.ErrorCode,
                    failure.CanRetry,
                    (int)executionResult.StatusCode,
                    executionResult.ContentType,
                    executionResult.Body.LongLength);
            }

            string responseText;
            try
            {
                responseText = ParseResponse(protocol, executionResult.Body);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return CreateFailureResult(
                    request,
                    summary,
                    progress,
                    startedAt,
                    exception is JsonException ? "invalid_json" : "invalid_response",
                    false,
                    (int)executionResult.StatusCode,
                    executionResult.ContentType,
                    executionResult.Body.LongLength);
            }

            var truncated = Truncate(responseText, request.MaxDisplayCharacters);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var statusCode = (int)executionResult.StatusCode;
            var providerResult = new ProviderTestResult(
                request.RequestId,
                ProviderTestStatus.Completed,
                summary,
                statusCode,
                executionResult.ContentType,
                elapsedMs,
                executionResult.Body.LongLength,
                truncated.Text,
                truncated.IsTruncated);

            progress?.Report(new ProviderTestProgress(
                request.RequestId,
                ProviderTestStatus.Completed,
                AccumulatedCharacters: truncated.Text.Length,
                IsTruncated: truncated.IsTruncated,
                StatusCode: statusCode,
                ContentType: executionResult.ContentType,
                ResponseBytes: executionResult.Body.LongLength));
            logger.LogInformation(
                "Provider 测试完成 {ProviderId}/{ModelId} {Protocol} {Path} {StatusCode} {ContentType} {ResponseBytes}B {UseProxy} {ElapsedMs}ms",
                request.ProviderId,
                request.ModelId,
                protocol,
                path,
                statusCode,
                executionResult.ContentType,
                executionResult.Body.LongLength,
                request.UseProxy,
                elapsedMs);
            return providerResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "cancelled",
                false,
                status: ProviderTestStatus.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "timeout",
                true);
        }
        catch (UriFormatException)
        {
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "invalid_url",
                false);
        }
        catch (HttpRequestException)
        {
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "network_error",
                true);
        }
    }

    private ProviderTestResult CreateFailureResult(
        ProviderTestRequest request,
        ProviderTestSummary summary,
        IProgress<ProviderTestProgress>? progress,
        long startedAt,
        string errorCode,
        bool canRetry,
        int? statusCode = null,
        string? contentType = null,
        long responseBytes = 0,
        ProviderTestStatus status = ProviderTestStatus.Failed)
    {
        var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        progress?.Report(new ProviderTestProgress(
            request.RequestId,
            status,
            StatusCode: statusCode,
            ContentType: contentType,
            ResponseBytes: responseBytes));

        if (status == ProviderTestStatus.Cancelled)
        {
            logger.LogInformation(
                "Provider 测试取消 {ProviderId}/{ModelId} {Protocol} {Path} {UseProxy} {ElapsedMs}ms",
                request.ProviderId,
                request.ModelId,
                summary.Protocol,
                summary.Path,
                request.UseProxy,
                elapsedMs);
        }
        else
        {
            logger.LogWarning(
                "Provider 测试失败 {ProviderId}/{ModelId} {Protocol} {Path} {StatusCode} {ContentType} {ResponseBytes}B {UseProxy} {ElapsedMs}ms {ErrorCode}",
                request.ProviderId,
                request.ModelId,
                summary.Protocol,
                summary.Path,
                statusCode,
                contentType,
                responseBytes,
                request.UseProxy,
                elapsedMs,
                errorCode);
        }

        return new ProviderTestResult(
            request.RequestId,
            status,
            summary,
            statusCode,
            contentType,
            elapsedMs,
            responseBytes,
            ErrorCode: errorCode,
            CanRetry: canRetry);
    }

    private static (string ErrorCode, bool CanRetry) MapHttpFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ("auth_failed", false),
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => ("endpoint_error", false),
        HttpStatusCode.RequestTimeout => ("timeout", true),
        HttpStatusCode.TooManyRequests => ("rate_limited", true),
        >= HttpStatusCode.InternalServerError => ("upstream_error", true),
        _ => ("request_rejected", false),
    };
    private static HttpRequestMessage BuildRequest(ProviderTestRequest request, string protocol, string path)
    {
        var endpoint = new Uri(request.BaseUrl.TrimEnd('/') + path, UriKind.Absolute);
        object body = protocol switch
        {
            "anthropic" => new
            {
                model = request.ModelId,
                max_tokens = 1024,
                messages = new[] { new { role = "user", content = request.Prompt } },
                stream = false,
            },
            "openai_responses" => new
            {
                model = request.ModelId,
                input = request.Prompt,
                stream = false,
            },
            _ => new
            {
                model = request.ModelId,
                messages = new[] { new { role = "user", content = request.Prompt } },
                stream = false,
            },
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        foreach (var header in request.Headers)
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (protocol == "anthropic")
        {
            httpRequest.Headers.Remove("x-api-key");
            httpRequest.Headers.Remove("anthropic-version");
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                httpRequest.Headers.TryAddWithoutValidation("x-api-key", request.ApiKey.Trim());
            httpRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
        }

        return httpRequest;
    }

    private static string ResolveProtocol(ProviderTestRequest request) =>
        string.Equals(request.ApiMode, "anthropic", StringComparison.OrdinalIgnoreCase)
            ? "anthropic"
            : string.Equals(request.EndpointFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? "openai_responses"
                : "openai_chat";

    private static string ResolvePath(string protocol) => protocol switch
    {
        "anthropic" => "/v1/messages",
        "openai_responses" => "/responses",
        _ => "/chat/completions",
    };

    private static string ParseResponse(string protocol, byte[] body)
    {
        var root = JsonNode.Parse(body) ?? throw new JsonException("响应 JSON 为空。");
        return protocol switch
        {
            "anthropic" => ParseAnthropic(root),
            "openai_responses" => ParseOpenAiResponses(root),
            _ => ParseOpenAiChat(root),
        };
    }

    private static string ParseOpenAiChat(JsonNode root) =>
        root["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";

    private static string ParseOpenAiResponses(JsonNode root)
    {
        if (root["output_text"] is JsonValue outputText
            && outputText.TryGetValue<string>(out var topLevelText))
        {
            return topLevelText;
        }

        if (root["output"] is not JsonArray output)
            return "";

        var builder = new StringBuilder();
        foreach (var item in output)
        {
            if (item?["content"] is not JsonArray content)
                continue;

            foreach (var contentItem in content)
                builder.Append(contentItem?["text"]?.GetValue<string>() ?? "");
        }

        return builder.ToString();
    }

    private static string ParseAnthropic(JsonNode root) =>
        string.Concat(root["content"]?.AsArray().Select(item => item?["text"]?.GetValue<string>() ?? "") ?? []);

    private static (string Text, bool IsTruncated) Truncate(string text, int maxDisplayCharacters)
    {
        var limit = Math.Max(0, maxDisplayCharacters);
        return text.Length <= limit
            ? (text, false)
            : (text[..limit], true);
    }
}