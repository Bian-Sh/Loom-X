using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
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

public sealed record ProviderTestRequest
{
    public ProviderTestRequest(
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
        int MaxDisplayCharacters)
    {
        this.RequestId = RequestId;
        this.ProviderId = ProviderId;
        this.ModelId = ModelId;
        this.BaseUrl = BaseUrl;
        this.ApiMode = ApiMode;
        this.EndpointFormat = EndpointFormat;
        this.ApiKey = ApiKey;
        this.Headers = CopyHeaders(Headers);
        this.UseProxy = UseProxy;
        this.Prompt = Prompt;
        this.Mode = Mode;
        this.MaxDisplayCharacters = MaxDisplayCharacters;
    }

    public string RequestId { get; init; }
    public string ProviderId { get; init; }
    public string ModelId { get; init; }
    public string BaseUrl { get; init; }
    public string ApiMode { get; init; }
    public string EndpointFormat { get; init; }
    public string? ApiKey { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public bool UseProxy { get; init; }
    public string Prompt { get; init; }
    public ProviderTestMode Mode { get; init; }
    public int MaxDisplayCharacters { get; init; }

    private static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
            copy[header.Key] = header.Value;
        return new ReadOnlyDictionary<string, string>(copy);
    }
}

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
    private static readonly JsonSerializerOptions DisplayJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient httpClient;
    private readonly ILogger<ProviderTestService> logger;
    private readonly IProviderExecutionPipeline executionPipeline;
    private readonly Func<CancellationToken, Task<UpdateProxySettings>> proxySettingsReader;
    private readonly Func<HttpClientHandler, HttpClient> proxyHttpClientFactory;

    public ProviderTestService(
        HttpClient httpClient,
        ILogger<ProviderTestService>? logger = null,
        IProviderExecutionPipeline? executionPipeline = null,
        Func<CancellationToken, Task<UpdateProxySettings>>? proxySettingsReader = null,
        Func<HttpClientHandler, HttpClient>? proxyHttpClientFactory = null)
    {
        this.httpClient = httpClient;
        this.logger = logger ?? NullLogger<ProviderTestService>.Instance;
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
        this.proxySettingsReader = proxySettingsReader ?? (_ => Task.FromResult(
            new UpdateProxySettings(false, "direct", string.Empty, 0, null, null)));
        this.proxyHttpClientFactory = proxyHttpClientFactory ?? (handler => new HttpClient(handler));
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
            false,
            "direct",
            CreateCliSummary(request.Headers),
            request.Headers.Count);


        var startedAt = Stopwatch.GetTimestamp();
        logger.LogInformation(
            "Provider 测试准备 {ProviderId}/{ModelId} {Protocol} {Path} {Mode} {RequestedProxy} {CustomHeaderCount}",
            request.ProviderId,
            request.ModelId,
            protocol,
            path,
            request.Mode,
            request.UseProxy,
            request.Headers.Count);
        progress?.Report(new ProviderTestProgress(request.RequestId, ProviderTestStatus.Sending));

        try
        {
            var proxySelection = await ResolveProxySelectionAsync(request.UseProxy, cancellationToken);
            summary = summary with
            {
                UseProxy = proxySelection.UseProxy,
                ProxySummary = proxySelection.Mode,
            };
            logger.LogInformation(
                "Provider 测试开始 {ProviderId}/{ModelId} {Protocol} {Path} {Mode} {UseProxy}",
                request.ProviderId,
                request.ModelId,
                protocol,
                path,
                request.Mode,
                summary.UseProxy);
            using var clientLease = CreateHttpClientLease(proxySelection);
            using var httpRequest = BuildRequest(request, protocol, path);
            LogEndpointAnomaly(request, httpRequest.RequestUri);
            logger.LogInformation(
                "Provider 测试发送 {ProviderId}/{ModelId} {Protocol} {Path} {Mode} {UseProxy}",
                request.ProviderId,
                request.ModelId,
                protocol,
                path,
                request.Mode,
                summary.UseProxy);
            if (request.Mode == ProviderTestMode.Streaming)
            {
                return await ExecuteStreamingRequestAsync(
                    clientLease.Client,
                    httpRequest,
                    request,
                    summary,
                    progress,
                    startedAt,
                    protocol,
                    path,
                    cancellationToken);
            }

            var executionResult = await executionPipeline.ExecuteAsync(
                clientLease.Client,
                httpRequest,
                new ProviderExecutionContext(request.ProviderId, request.ModelId, request.ApiMode, path),
                cancellationToken);
            logger.LogInformation(
                "Provider 测试收到响应 {ProviderId}/{ModelId} {Protocol} {Path} {StatusCode} {ContentType} {ResponseBytes}B",
                request.ProviderId,
                request.ModelId,
                protocol,
                path,
                (int)executionResult.StatusCode,
                executionResult.ContentType,
                executionResult.Body.LongLength);

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
                    executionResult.Body.LongLength,
                    responseText: FormatResponseBody(executionResult.Body, request.MaxDisplayCharacters));
            }

            string responseText;
            try
            {
                responseText = ParseResponse(protocol, executionResult.Body);
            }
            catch (Exception exception) when (exception is JsonException or InvalidProviderResponseException)
            {
                logger.LogWarning(exception, "Provider 测试解析失败 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
                return CreateFailureResult(
                    request,
                    summary,
                    progress,
                    startedAt,
                    exception is JsonException ? "invalid_json" : "invalid_response",
                    false,
                    (int)executionResult.StatusCode,
                    executionResult.ContentType,
                    executionResult.Body.LongLength,
                    responseText: FormatResponseBody(executionResult.Body, request.MaxDisplayCharacters));
            }

            logger.LogInformation(
                "Provider 测试解析完成 {ProviderId}/{ModelId} {Protocol} {Path} {Characters}",
                request.ProviderId,
                request.ModelId,
                protocol,
                path,
                responseText.Length);
            var truncated = Truncate(responseText, request.MaxDisplayCharacters);
            return CreateSuccessResult(
                request,
                summary,
                progress,
                startedAt,
                truncated.Text,
                truncated.IsTruncated,
                (int)executionResult.StatusCode,
                executionResult.ContentType,
                executionResult.Body.LongLength);
        }
        catch (InvalidProxyConfigurationException exception)
        {
            logger.LogWarning(exception, "Provider 测试代理配置无效 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "invalid_proxy_configuration",
                false,
                errorDetail: "代理配置无效。");
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(exception, "Provider 测试取消 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "cancelled",
                false,
                status: ProviderTestStatus.Cancelled);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "Provider 测试超时 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "timeout",
                true,
                errorDetail: "请求超时。");
        }
        catch (UriFormatException exception)
        {
            logger.LogWarning(exception, "Provider 测试 URL 无效 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "invalid_url",
                false,
                errorDetail: exception.Message);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Provider 测试网络异常 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "network_error",
                true,
                errorDetail: exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Provider 测试未处理异常 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                "internal_error",
                false,
                errorDetail: "测试请求发生内部错误。");
        }
    }

    private async Task<ProxySelection> ResolveProxySelectionAsync(
        bool useProxy,
        CancellationToken cancellationToken)
    {
        if (!useProxy)
            return ProxySelection.Direct;

        var settings = await proxySettingsReader(cancellationToken)
            ?? throw new InvalidProxyConfigurationException();
        var mode = settings.ProxyMode?.Trim().ToLowerInvariant();
        return mode switch
        {
            "direct" => ProxySelection.Direct,
            "system" => new ProxySelection("system", true, settings),
            "custom" => new ProxySelection("custom", true, settings),
            _ => throw new InvalidProxyConfigurationException(),
        };
    }

    private HttpClientLease CreateHttpClientLease(ProxySelection selection)
    {
        if (!selection.UseProxy)
            return new HttpClientLease(httpClient, ownsClient: false);

        var handler = new HttpClientHandler { UseProxy = true };
        if (string.Equals(selection.Mode, "custom", StringComparison.Ordinal))
        {
            var settings = selection.Settings ?? throw new InvalidProxyConfigurationException();
            if (!Uri.TryCreate(settings.ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri)
                || proxyUri.Scheme is not ("http" or "https")
                || settings.ProxyPort is < 1 or > 65535)
            {
                handler.Dispose();
                throw new InvalidProxyConfigurationException();
            }

            var proxy = new WebProxy($"{proxyUri.Scheme}://{proxyUri.Host}:{settings.ProxyPort}");
            var password = settings.ProxyPassword;
            if (!string.IsNullOrWhiteSpace(settings.ProxyUsername) || !string.IsNullOrWhiteSpace(password))
            {
                proxy.Credentials = new NetworkCredential(
                    settings.ProxyUsername ?? string.Empty,
                    password ?? string.Empty);
            }
            handler.Proxy = proxy;
        }

        try
        {
            return new HttpClientLease(proxyHttpClientFactory(handler), ownsClient: true);
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }

    private static string? CreateCliSummary(IReadOnlyDictionary<string, string> headers)
    {
        var identity = CliIdentityService.DetectCliIdentity(headers);
        if (identity is null)
            return null;

        var displayName = CliIdentityService.GetProfile(identity.Value).DisplayName;
        var version = CliIdentityService.DetectCliVersion(headers, identity.Value);
        return string.IsNullOrWhiteSpace(version) ? displayName : $"{displayName} {version}";
    }

    private async Task<ProviderTestResult> ExecuteStreamingRequestAsync(
        HttpClient client,
        HttpRequestMessage httpRequest,
        ProviderTestRequest request,
        ProviderTestSummary summary,
        IProgress<ProviderTestProgress>? progress,
        long startedAt,
        string protocol,
        string path,
        CancellationToken cancellationToken)
    {
        await using var executionResult = await executionPipeline.ExecuteStreamingAsync(
            client,
            httpRequest,
            new ProviderExecutionContext(request.ProviderId, request.ModelId, request.ApiMode, path),
            cancellationToken);

        var statusCode = (int)executionResult.StatusCode;
        logger.LogInformation(
            "Provider 测试收到响应 {ProviderId}/{ModelId} {Protocol} {Path} {StatusCode} {ContentType}",
            request.ProviderId,
            request.ModelId,
            protocol,
            path,
            statusCode,
            executionResult.ContentType);
        if (!executionResult.IsSuccess)
        {
            var rawResponse = await ReadResponseTextAsync(executionResult.Body, cancellationToken);
            var failure = MapHttpFailure(executionResult.StatusCode);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                failure.ErrorCode,
                failure.CanRetry,
                statusCode,
                executionResult.ContentType,
                Encoding.UTF8.GetByteCount(rawResponse),
                responseText: FormatResponseText(rawResponse, request.MaxDisplayCharacters));
        }

        var builder = new StringBuilder();
        var isTruncated = false;
        long responseBytes = 0;
        string? eventName = null;
        var dataLines = new List<string>();
        var completed = false;

        using var reader = new StreamReader(
            executionResult.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                responseBytes += Encoding.UTF8.GetByteCount(line) + 1;
                if (line.Length == 0)
                {
                    if (dataLines.Count > 0)
                    {
                        completed = ProcessStreamingFrame(
                            protocol,
                            eventName,
                            string.Join('\n', dataLines),
                            request,
                            progress,
                            statusCode,
                            executionResult.ContentType,
                            responseBytes,
                            builder,
                            ref isTruncated);
                    }

                    eventName = null;
                    dataLines.Clear();
                    if (completed)
                        break;
                    continue;
                }

                if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                {
                    eventName = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var data = line[5..];
                    dataLines.Add(data.StartsWith(' ') ? data[1..] : data);
                }
            }

            if (!completed && dataLines.Count > 0)
            {
                _ = ProcessStreamingFrame(
                    protocol,
                    eventName,
                    string.Join('\n', dataLines),
                    request,
                    progress,
                    statusCode,
                    executionResult.ContentType,
                    responseBytes,
                    builder,
                    ref isTruncated);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidProviderResponseException)
        {
            logger.LogWarning(exception, "Provider 测试流式解析失败 {ProviderId}/{ModelId} {Protocol} {Path}", request.ProviderId, request.ModelId, protocol, path);
            return CreateFailureResult(
                request,
                summary,
                progress,
                startedAt,
                exception is JsonException ? "invalid_json" : "invalid_response",
                false,
                statusCode,
                executionResult.ContentType,
                responseBytes,
                responseText: FormatResponseText(string.Join('\n', dataLines), request.MaxDisplayCharacters));
        }

        logger.LogInformation(
            "Provider 测试解析完成 {ProviderId}/{ModelId} {Protocol} {Path} {Characters}",
            request.ProviderId,
            request.ModelId,
            protocol,
            path,
            builder.Length);
        return CreateSuccessResult(
            request,
            summary,
            progress,
            startedAt,
            builder.ToString(),
            isTruncated,
            statusCode,
            executionResult.ContentType,
            responseBytes);
    }

    private static bool ProcessStreamingFrame(
        string protocol,
        string? eventName,
        string data,
        ProviderTestRequest request,
        IProgress<ProviderTestProgress>? progress,
        int statusCode,
        string? contentType,
        long responseBytes,
        StringBuilder builder,
        ref bool isTruncated)
    {
        var frame = ParseStreamingFrame(protocol, eventName, data);
        if (frame.IsCompleted)
            return true;
        if (string.IsNullOrEmpty(frame.TextDelta))
            return false;

        var limit = Math.Max(0, request.MaxDisplayCharacters);
        var remaining = Math.Max(0, limit - builder.Length);
        var appendedLength = Math.Min(remaining, frame.TextDelta.Length);
        var appended = appendedLength == 0 ? string.Empty : frame.TextDelta[..appendedLength];
        if (appendedLength < frame.TextDelta.Length)
            isTruncated = true;
        builder.Append(appended);

        progress?.Report(new ProviderTestProgress(
            request.RequestId,
            ProviderTestStatus.Sending,
            appended,
            builder.Length,
            isTruncated,
            statusCode,
            contentType,
            responseBytes));
        return false;
    }

    private static StreamingFrame ParseStreamingFrame(string protocol, string? eventName, string data)
    {
        if (string.Equals(data.Trim(), "[DONE]", StringComparison.OrdinalIgnoreCase))
            return new StreamingFrame(string.Empty, true);

        return protocol switch
        {
            "openai_responses" => ParseOpenAiResponsesFrame(eventName, data),
            "anthropic" => ParseAnthropicFrame(eventName, data),
            _ => ParseOpenAiChatFrame(eventName, data),
        };
    }

    private static StreamingFrame ParseOpenAiChatFrame(string? eventName, string data)
    {
        if (!string.IsNullOrEmpty(eventName))
            return default;

        var root = JsonNode.Parse(data) as JsonObject ?? throw new InvalidProviderResponseException();
        if (root["choices"] is not JsonArray { Count: > 0 } choices
            || choices[0] is not JsonObject choice
            || choice["delta"] is not JsonObject delta)
        {
            throw new InvalidProviderResponseException();
        }

        if (delta["content"] is null)
            return default;
        if (delta["content"] is not JsonValue content || !content.TryGetValue<string>(out var text))
            throw new InvalidProviderResponseException();
        return new StreamingFrame(text, false);
    }

    private static StreamingFrame ParseOpenAiResponsesFrame(string? eventName, string data)
    {
        if (string.Equals(eventName, "response.completed", StringComparison.Ordinal))
            return new StreamingFrame(string.Empty, true);
        if (!string.IsNullOrEmpty(eventName)
            && !string.Equals(eventName, "response.output_text.delta", StringComparison.Ordinal))
        {
            return default;
        }

        var root = JsonNode.Parse(data) as JsonObject ?? throw new InvalidProviderResponseException();
        var type = root["type"]?.GetValue<string>();
        if (string.Equals(type, "response.completed", StringComparison.Ordinal))
            return new StreamingFrame(string.Empty, true);
        if (!string.Equals(eventName ?? type, "response.output_text.delta", StringComparison.Ordinal))
            return default;
        if (root["delta"] is not JsonValue delta || !delta.TryGetValue<string>(out var text))
            throw new InvalidProviderResponseException();
        return new StreamingFrame(text, false);
    }

    private static StreamingFrame ParseAnthropicFrame(string? eventName, string data)
    {
        if (string.Equals(eventName, "message_stop", StringComparison.Ordinal))
            return new StreamingFrame(string.Empty, true);
        if (!string.IsNullOrEmpty(eventName)
            && !string.Equals(eventName, "content_block_delta", StringComparison.Ordinal))
        {
            return default;
        }

        var root = JsonNode.Parse(data) as JsonObject ?? throw new InvalidProviderResponseException();
        var type = root["type"]?.GetValue<string>();
        if (string.Equals(type, "message_stop", StringComparison.Ordinal))
            return new StreamingFrame(string.Empty, true);
        if (!string.Equals(eventName ?? type, "content_block_delta", StringComparison.Ordinal))
            return default;
        if (root["delta"] is not JsonObject delta
            || delta["text"] is not JsonValue textValue
            || !textValue.TryGetValue<string>(out var text))
        {
            throw new InvalidProviderResponseException();
        }
        return new StreamingFrame(text, false);
    }

    private ProviderTestResult CreateSuccessResult(
        ProviderTestRequest request,
        ProviderTestSummary summary,
        IProgress<ProviderTestProgress>? progress,
        long startedAt,
        string responseText,
        bool isTruncated,
        int statusCode,
        string? contentType,
        long responseBytes)
    {
        var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        progress?.Report(new ProviderTestProgress(
            request.RequestId,
            ProviderTestStatus.Completed,
            AccumulatedCharacters: responseText.Length,
            IsTruncated: isTruncated,
            StatusCode: statusCode,
            ContentType: contentType,
            ResponseBytes: responseBytes));
        logger.LogInformation(
            "Provider 测试完成 {ProviderId}/{ModelId} {Protocol} {Path} {StatusCode} {ContentType} {ResponseBytes}B {UseProxy} {ElapsedMs}ms",
            request.ProviderId,
            request.ModelId,
            summary.Protocol,
            summary.Path,
            statusCode,
            contentType,
            responseBytes,
            summary.UseProxy,
            elapsedMs);
        return new ProviderTestResult(
            request.RequestId,
            ProviderTestStatus.Completed,
            summary,
            statusCode,
            contentType,
            elapsedMs,
            responseBytes,
            responseText,
            isTruncated);
    }

    private readonly record struct StreamingFrame(string? TextDelta, bool IsCompleted);

    private sealed record ProxySelection(string Mode, bool UseProxy, UpdateProxySettings? Settings)
    {
        public static ProxySelection Direct { get; } = new("direct", false, null);
    }

    private sealed class HttpClientLease(HttpClient client, bool ownsClient) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            if (ownsClient)
                Client.Dispose();
        }
    }

    private sealed class InvalidProxyConfigurationException : Exception
    {
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
        ProviderTestStatus status = ProviderTestStatus.Failed,
        string? responseText = null,
        string? errorDetail = null)
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
                summary.UseProxy,
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
                summary.UseProxy,
                elapsedMs,
                errorCode);
        }

        var displayedResponse = string.IsNullOrWhiteSpace(responseText)
            ? CreateErrorResponse(errorCode, statusCode, errorDetail, request.MaxDisplayCharacters)
            : responseText;
        return new ProviderTestResult(
            request.RequestId,
            status,
            summary,
            statusCode,
            contentType,
            elapsedMs,
            responseBytes,
            ResponseText: displayedResponse,
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
        var endpoint = ProviderRouteEndpointResolver.Resolve(request.BaseUrl, path);
        object body = protocol switch
        {
            "anthropic" => new
            {
                model = request.ModelId,
                max_tokens = 1024,
                messages = new[] { new { role = "user", content = request.Prompt } },
                stream = request.Mode == ProviderTestMode.Streaming,
            },
            "openai_responses" => new
            {
                model = request.ModelId,
                input = request.Prompt,
                stream = request.Mode == ProviderTestMode.Streaming,
            },
            _ => new
            {
                model = request.ModelId,
                messages = new[] { new { role = "user", content = request.Prompt } },
                stream = request.Mode == ProviderTestMode.Streaming,
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

    internal static string ResolveEndpoint(string baseUrl, string apiMode, string endpointFormat)
    {
        var protocol = string.Equals(apiMode, "anthropic", StringComparison.OrdinalIgnoreCase)
            ? "anthropic"
            : string.Equals(endpointFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? "openai_responses"
                : "openai_chat";
        return ProviderRouteEndpointResolver.Resolve(baseUrl, ResolvePath(protocol)).AbsoluteUri;
    }

    private void LogEndpointAnomaly(ProviderTestRequest request, Uri? endpoint)
    {
        if (!ProviderRouteEndpointResolver.HasRepeatedVersionSegment(endpoint)) return;
        logger.LogWarning(
            "Provider 测试上游地址存在重复版本段 {ProviderId}/{ModelId} {EndpointPath}",
            request.ProviderId,
            request.ModelId,
            endpoint!.AbsolutePath);
    }

    private static string ParseResponse(string protocol, byte[] body)
    {
        var root = JsonNode.Parse(body);
        if (root is not JsonObject response)
            throw new InvalidProviderResponseException();

        return protocol switch
        {
            "anthropic" => ParseAnthropic(response),
            "openai_responses" => ParseOpenAiResponses(response),
            _ => ParseOpenAiChat(response),
        };
    }

    private static string ParseOpenAiChat(JsonObject root)
    {
        if (root["choices"] is not JsonArray { Count: > 0 } choices
            || choices[0] is not JsonObject choice
            || choice["message"] is not JsonObject message
            || message["content"] is not JsonValue content
            || !content.TryGetValue<string>(out var text))
        {
            throw new InvalidProviderResponseException();
        }

        return text;
    }

    private static string ParseOpenAiResponses(JsonObject root)
    {
        if (root["output_text"] is JsonValue outputText
            && outputText.TryGetValue<string>(out var topLevelText))
        {
            return topLevelText;
        }

        if (root["output"] is not JsonArray { Count: > 0 } output)
            throw new InvalidProviderResponseException();

        var builder = new StringBuilder();
        var foundText = false;
        foreach (var item in output)
        {
            if (item is not JsonObject outputItem)
                throw new InvalidProviderResponseException();
            if (outputItem["content"] is not JsonArray content)
                continue;

            foreach (var contentItem in content)
            {
                if (contentItem is not JsonObject contentObject)
                    throw new InvalidProviderResponseException();
                if (contentObject["text"] is null)
                    continue;
                if (contentObject["text"] is not JsonValue textValue
                    || !textValue.TryGetValue<string>(out var text))
                {
                    throw new InvalidProviderResponseException();
                }

                builder.Append(text);
                foundText = true;
            }
        }

        return foundText ? builder.ToString() : throw new InvalidProviderResponseException();
    }

    private static string ParseAnthropic(JsonObject root)
    {
        if (root["content"] is not JsonArray { Count: > 0 } content)
            throw new InvalidProviderResponseException();

        var builder = new StringBuilder();
        var foundText = false;
        foreach (var item in content)
        {
            if (item is not JsonObject contentItem)
                throw new InvalidProviderResponseException();
            if (contentItem["text"] is null)
                continue;
            if (contentItem["text"] is not JsonValue textValue
                || !textValue.TryGetValue<string>(out var text))
            {
                throw new InvalidProviderResponseException();
            }

            builder.Append(text);
            foundText = true;
        }

        return foundText ? builder.ToString() : throw new InvalidProviderResponseException();
    }

    private sealed class InvalidProviderResponseException : Exception
    {
    }

    private static string FormatResponseBody(byte[] body, int maxDisplayCharacters)
        => FormatResponseText(Encoding.UTF8.GetString(body), maxDisplayCharacters);

    private static string FormatResponseText(string text, int maxDisplayCharacters)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var display = text;
        try
        {
            display = JsonNode.Parse(text)?.ToJsonString(DisplayJsonOptions) ?? text;
        }
        catch (JsonException)
        {
        }

        return Truncate(display, maxDisplayCharacters).Text;
    }

    private static async Task<string> ReadResponseTextAsync(Stream body, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string CreateErrorResponse(
        string errorCode,
        int? statusCode,
        string? detail,
        int maxDisplayCharacters)
    {
        var error = new JsonObject
        {
            ["code"] = errorCode,
            ["message"] = string.IsNullOrWhiteSpace(detail) ? ErrorMessage(errorCode) : detail,
        };
        if (statusCode is not null)
            error["status_code"] = statusCode.Value;
        return FormatResponseText(new JsonObject { ["error"] = error }.ToJsonString(DisplayJsonOptions), maxDisplayCharacters);
    }

    private static string ErrorMessage(string errorCode) => errorCode switch
    {
        "auth_failed" => "上游鉴权失败。",
        "endpoint_error" => "上游端点不可用。",
        "timeout" => "请求超时。",
        "rate_limited" => "上游触发限流。",
        "upstream_error" => "上游服务异常。",
        "request_rejected" => "上游拒绝了请求。",
        "invalid_json" => "上游返回的内容不是有效 JSON。",
        "invalid_response" => "上游响应结构与当前协议不匹配。",
        "invalid_proxy_configuration" => "代理配置无效。",
        "invalid_url" => "请求地址无效。",
        "network_error" => "网络请求失败。",
        "cancelled" => "请求已取消。",
        "internal_error" => "测试请求发生内部错误。",
        _ => "请求失败。",
    };
    private static (string Text, bool IsTruncated) Truncate(string text, int maxDisplayCharacters)
    {
        var limit = Math.Max(0, maxDisplayCharacters);
        return text.Length <= limit
            ? (text, false)
            : (text[..limit], true);
    }
}