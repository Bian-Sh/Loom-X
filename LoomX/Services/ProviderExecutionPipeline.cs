using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Plugins;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// Provider 请求执行所需的安全上下文。请求体和密钥仍由调用方构造，管线负责发送、响应收集与 Router 插件处理。
/// </summary>
public sealed record ProviderExecutionContext(
    string ProviderId,
    string ModelId,
    string ApiMode,
    string UpstreamPath,
    bool NormalizeOpenAiFinishReasons = false);

/// <summary>Provider 响应的统一结果，供网关转发和小助手解析共同消费。</summary>
public sealed record ProviderExecutionResult(
    HttpStatusCode StatusCode,
    string? ContentType,
    IReadOnlyDictionary<string, string[]> Headers,
    byte[] Body,
    bool WasNormalized)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and <= 299;

    public bool IsRetryable => (int)StatusCode is 408 or 429 or >= 500;

    public string BodyText => Encoding.UTF8.GetString(Body);
}

/// <summary>调用方负责释放响应和正文流；成功响应不预读正文。</summary>
public sealed class ProviderStreamingResult(HttpResponseMessage response, Stream body) : IAsyncDisposable
{
    public HttpStatusCode StatusCode => response.StatusCode;
    public string? ContentType => response.Content.Headers.ContentType?.MediaType;
    public Stream Body => body;
    public bool IsSuccess => response.IsSuccessStatusCode;
    public TimeSpan? RetryAfter => response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    public async ValueTask DisposeAsync()
    {
        await body.DisposeAsync();
        response.Dispose();
    }
}

public interface IProviderExecutionPipeline
{
    Task<ProviderExecutionResult> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken);

    Task<ProviderStreamingResult> ExecuteStreamingAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 统一的 Provider 请求后半段：发送、读取响应、收集元数据和协议级轻量规范化。
/// 不记录请求或响应正文，调用方负责将结果映射到自己的入口协议。
/// </summary>
public sealed class ProviderExecutionPipeline(
    ILogger<ProviderExecutionPipeline>? logger = null,
    IPipeline? requestPipeline = null,
    IPipeline? responsePipeline = null) : IProviderExecutionPipeline
{
    public async Task<ProviderStreamingResult> ExecuteStreamingAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        await ApplyRequestPipelineAsync(request, context, cancellationToken);
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            foreach (var encoding in response.Content.Headers.ContentEncoding.Reverse())
            {
                body = encoding.Trim().ToLowerInvariant() switch
                {
                    "gzip" => new GZipStream(body, CompressionMode.Decompress),
                    "deflate" => new DeflateStream(body, CompressionMode.Decompress),
                    "br" => new BrotliStream(body, CompressionMode.Decompress),
                    "identity" => body,
                    _ => throw new NotSupportedException($"不支持的响应压缩格式：{encoding}"),
                };
            }

            if (responsePipeline is not null && response.IsSuccessStatusCode)
            {
                body = string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase)
                    ? new SsePipelineRestoringStream(
                        body,
                        responsePipeline,
                        context,
                        logger,
                        cancellationToken)
                    : new PipelineRestoringStream(
                        body,
                        responsePipeline,
                        context,
                        logger,
                        cancellationToken);
            }

            return new ProviderStreamingResult(response, body);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<ProviderExecutionResult> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        await ApplyRequestPipelineAsync(request, context, cancellationToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var headers = CaptureHeaders(response);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (TryDecompress(body, response.Content.Headers.ContentEncoding, out var decompressedBody))
        {
            body = decompressedBody;
            headers = RemoveContentEncodingHeaders(headers);
        }
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var normalized = false;

        if (context.NormalizeOpenAiFinishReasons
            && string.Equals(context.ApiMode, "openai", StringComparison.OrdinalIgnoreCase))
        {
            normalized = ProviderResponseNormalizer.TryNormalizeOpenAiFinishReasons(body, contentType, out body);
        }

        if (responsePipeline is not null && response.IsSuccessStatusCode && body.Length > 0)
        {
            var originalBody = body;
            if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                await using var source = new MemoryStream(body, writable: false);
                await using var restoring = new SsePipelineRestoringStream(
                    source, responsePipeline, context, logger, cancellationToken);
                using var restoredOutput = new MemoryStream();
                await restoring.CopyToAsync(restoredOutput, cancellationToken);
                body = restoredOutput.ToArray();
            }
            else
            {
                var restored = await ApplyResponsePipelineAsync(
                    Encoding.UTF8.GetString(body), context, cancellationToken);
                if (restored.Modified) body = Encoding.UTF8.GetBytes(restored.Payload);
            }

            if (!body.AsSpan().SequenceEqual(originalBody))
            {
                headers = RemoveModifiedEntityHeaders(headers);
                normalized = true;
            }
        }

        return new ProviderExecutionResult(response.StatusCode, contentType, headers, body, normalized);
    }

    /// <summary>
    /// Router Request Pipeline：只处理请求正文，不向插件暴露或修改 Router 管理的认证 Header。
    /// Blocked 或执行异常时 fail closed，原始正文不会发送给上游。
    /// </summary>
    private async Task ApplyRequestPipelineAsync(
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (requestPipeline is null || request.Content is null) return;

        string payload;
        try
        {
            payload = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Router 请求正文读取失败，已阻止发送 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 请求正文未完成数据安全处理，已阻止发送。", exception);
        }

        PipelineResult result;
        try
        {
            result = await requestPipeline.ExecuteAsync(payload, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Router Request Pipeline 执行失败，已阻止发送 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 请求未完成数据安全处理，已阻止发送。", exception);
        }

        if (result.Outcome == PipelineOutcome.Blocked)
        {
            logger?.LogWarning(
                "Router Request Pipeline 阻止请求发送 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 请求未通过数据安全处理，已阻止发送。");
        }

        if (result.Outcome != PipelineOutcome.Modified) return;

        var originalContent = request.Content;
        var replacement = new ByteArrayContent(Encoding.UTF8.GetBytes(result.Payload));
        foreach (var header in originalContent.Headers)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content = replacement;
        originalContent.Dispose();
        logger?.LogInformation(
            "Router Request Pipeline 已处理请求正文 {ProviderId}/{ModelId} {Path} {PayloadBytes}",
            context.ProviderId,
            context.ModelId,
            context.UpstreamPath,
            Encoding.UTF8.GetByteCount(result.Payload));
    }

    private async ValueTask<(string Payload, bool Modified)> ApplyResponsePipelineAsync(
        string payload,
        ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        PipelineResult result;
        try
        {
            result = await responsePipeline!.ExecuteAsync(payload, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Router Response Pipeline 执行失败，已阻止返回 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 响应未完成本地凭据恢复，已阻止返回。", exception);
        }

        if (result.Outcome == PipelineOutcome.Blocked)
        {
            logger?.LogWarning(
                "Router Response Pipeline 阻止响应返回 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 响应未通过本地凭据恢复，已阻止返回。");
        }

        return (result.Payload, result.Outcome == PipelineOutcome.Modified);
    }

    private static IReadOnlyDictionary<string, string[]> RemoveModifiedEntityHeaders(
        IReadOnlyDictionary<string, string[]> headers)
    {
        var result = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Content-Length", "Content-MD5", "Digest", "Content-Digest", "ETag" })
            result.Remove(name);
        return result;
    }

    private static bool TryDecompress(
        byte[] body,
        ICollection<string> encodings,
        out byte[] decompressedBody)
    {
        decompressedBody = body;
        if (body.Length == 0 || encodings.Count == 0) return false;

        var current = body;
        foreach (var encoding in encodings.Reverse())
        {
            var normalized = encoding.Trim();
            if (normalized.Length == 0 || normalized.Equals("identity", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                current = normalized.ToLowerInvariant() switch
                {
                    "gzip" => DecompressSingle(current, static stream => new GZipStream(stream, CompressionMode.Decompress)),
                    "deflate" => DecompressSingle(current, static stream => new DeflateStream(stream, CompressionMode.Decompress)),
                    "br" => DecompressSingle(current, static stream => new BrotliStream(stream, CompressionMode.Decompress)),
                    _ => throw new NotSupportedException(),
                };
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        decompressedBody = current;
        return true;
    }

    private static byte[] DecompressSingle(byte[] body, Func<Stream, Stream> createDecoder)
    {
        using var input = new MemoryStream(body, writable: false);
        using var decoder = createDecoder(input);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        return output.ToArray();
    }

    private static IReadOnlyDictionary<string, string[]> RemoveContentEncodingHeaders(
        IReadOnlyDictionary<string, string[]> headers)
    {
        var result = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase);
        result.Remove("Content-Encoding");
        result.Remove("Content-Length");
        return result;
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            headers[header.Key] = header.Value.ToArray();
        }

        return headers;
    }
}

/// <summary>
/// SSE 响应恢复包装器。按 JSON 字符串通道识别被模型拆到多个 delta 事件中的占位符，
/// 暂存相关事件直到 token 完整，再经 Response Pipeline 恢复并按原顺序输出。
/// </summary>
internal sealed class SsePipelineRestoringStream : Stream
{
    private const string PlaceholderMarker = "{{LOOMX_CREDENTIAL_";
    private readonly StreamReader reader;
    private readonly IPipeline pipeline;
    private readonly ProviderExecutionContext context;
    private readonly ILogger<ProviderExecutionPipeline>? logger;
    private readonly CancellationToken requestCancellationToken;
    private readonly Queue<byte> output = new();
    private readonly List<SseFrame> bufferedFrames = [];
    private readonly Dictionary<string, PendingChannel> pendingChannels = new(StringComparer.Ordinal);
    private bool completed;

    private sealed record PendingChannel(string Text);
    private sealed record SseFrame(string Prefix, string Suffix, JsonNode? Node, string? RawLine);

    public SsePipelineRestoringStream(
        Stream source,
        IPipeline pipeline,
        ProviderExecutionContext context,
        ILogger<ProviderExecutionPipeline>? logger,
        CancellationToken requestCancellationToken)
    {
        reader = new StreamReader(source, Encoding.UTF8, true, 4096, leaveOpen: false);
        this.pipeline = pipeline;
        this.context = context;
        this.logger = logger;
        this.requestCancellationToken = requestCancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken, cancellationToken);
        while (output.Count == 0 && !completed)
            await FillOutputAsync(linked.Token);

        var length = Math.Min(buffer.Length, output.Count);
        for (var index = 0; index < length; index++) buffer.Span[index] = output.Dequeue();
        return length;
    }

    private async Task FillOutputAsync(CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            completed = true;
            if (pendingChannels.Count > 0)
                throw new InvalidOperationException("Router 流式响应以未闭合凭据占位符结束，已阻止返回。");
            await FlushBufferedAsync(cancellationToken);
            return;
        }

        if (!TryParseDataFrame(line, out var frame))
        {
            if (pendingChannels.Count > 0) bufferedFrames.Add(new SseFrame(string.Empty, string.Empty, null, line));
            else EnqueueLine(line);
            return;
        }

        var hadPending = pendingChannels.Count > 0;
        var node = frame.Node!;
        await ProcessStringChannelsAsync(node, "$", cancellationToken);
        if (hadPending || pendingChannels.Count > 0)
        {
            bufferedFrames.Add(frame);
            if (pendingChannels.Count == 0) await FlushBufferedAsync(cancellationToken);
            return;
        }

        var restored = await ExecutePipelineAsync(node.ToJsonString(), cancellationToken);
        EnqueueLine(frame.Prefix + restored + frame.Suffix);
    }

    private async Task ProcessStringChannelsAsync(JsonNode node, string path, CancellationToken cancellationToken)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                var childPath = path + "/" + property.Key;
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    jsonObject[property.Key] = await ProcessStringValueAsync(childPath, text, cancellationToken);
                }
                else if (property.Value is not null)
                {
                    await ProcessStringChannelsAsync(property.Value, childPath, cancellationToken);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                if (jsonArray[index] is null) continue;
                var childPath = path + "/" + GetStableArrayKey(jsonArray[index]!, index);
                if (jsonArray[index] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    jsonArray[index] = await ProcessStringValueAsync(childPath, text, cancellationToken);
                }
                else
                {
                    await ProcessStringChannelsAsync(jsonArray[index]!, childPath, cancellationToken);
                }
            }
        }
    }

    private async Task<string> ProcessStringValueAsync(
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        var combined = pendingChannels.TryGetValue(path, out var pending) ? pending.Text + text : text;
        if (HasIncompletePlaceholderSuffix(combined))
        {
            if (combined.Length > 64)
                throw new InvalidOperationException("Router 流式响应中的凭据占位符超过合法长度，已阻止返回。");
            pendingChannels[path] = new PendingChannel(combined);
            return string.Empty;
        }

        if (pending is null) return text;
        pendingChannels.Remove(path);
        return await RestoreJsonStringAsync(combined, cancellationToken);
    }

    private async Task<string> RestoreJsonStringAsync(string value, CancellationToken cancellationToken)
    {
        var restored = await ExecutePipelineAsync(JsonSerializer.Serialize(value), cancellationToken);
        try { return JsonSerializer.Deserialize<string>(restored) ?? string.Empty; }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Router 流式响应恢复结果不是有效 JSON 字符串。", exception);
        }
    }

    private async Task FlushBufferedAsync(CancellationToken cancellationToken)
    {
        foreach (var frame in bufferedFrames)
        {
            if (frame.Node is null)
            {
                EnqueueLine(frame.RawLine ?? string.Empty);
                continue;
            }

            var restored = await ExecutePipelineAsync(frame.Node.ToJsonString(), cancellationToken);
            EnqueueLine(frame.Prefix + restored + frame.Suffix);
        }
        bufferedFrames.Clear();
        pendingChannels.Clear();
    }

    private async Task<string> ExecutePipelineAsync(string payload, CancellationToken cancellationToken)
    {
        PipelineResult result;
        try { result = await pipeline.ExecuteAsync(payload, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Router SSE Response Pipeline 执行失败，已阻止返回 {ProviderId}/{ModelId} {Path}",
                context.ProviderId, context.ModelId, context.UpstreamPath);
            throw new InvalidOperationException("Router 流式响应未完成本地凭据恢复，已阻止返回。", exception);
        }

        if (result.Outcome == PipelineOutcome.Blocked)
            throw new InvalidOperationException("Router 流式响应未通过本地凭据恢复，已阻止返回。");
        return result.Payload;
    }

    private static bool TryParseDataFrame(string line, out SseFrame frame)
    {
        frame = new SseFrame(string.Empty, string.Empty, null, line);
        if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        var data = line[5..];
        var whitespaceLength = data.Length - data.TrimStart().Length;
        var json = data.Trim();
        if (json.Length == 0 || string.Equals(json, "[DONE]", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return false;
            frame = new SseFrame(line[..5] + data[..whitespaceLength], string.Empty, node, line);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool HasIncompletePlaceholderSuffix(string value)
    {
        var markerIndex = value.LastIndexOf(PlaceholderMarker, StringComparison.Ordinal);
        if (markerIndex >= 0 && value.IndexOf("}}", markerIndex, StringComparison.Ordinal) < 0) return true;
        var max = Math.Min(value.Length, PlaceholderMarker.Length - 1);
        for (var length = max; length > 0; length--)
            if (PlaceholderMarker.StartsWith(value[^length..], StringComparison.Ordinal)) return true;
        return false;
    }

    private static string GetStableArrayKey(JsonNode node, int fallbackIndex)
    {
        if (node is JsonObject jsonObject && jsonObject["index"] is JsonValue indexValue)
        {
            if (indexValue.TryGetValue<int>(out var numericIndex)) return "index:" + numericIndex;
            if (indexValue.TryGetValue<string>(out var stringIndex)) return "index:" + stringIndex;
        }
        return fallbackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EnqueueLine(string line)
    {
        foreach (var item in Encoding.UTF8.GetBytes(line + "\n")) output.Enqueue(item);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) reader.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        reader.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// 非 SSE 流式响应恢复包装器。保留未闭合占位符尾部，避免 token 被网络读块拆分时漏恢复。
/// </summary>
internal sealed class PipelineRestoringStream : Stream
{
    private const string PlaceholderMarker = "{{LOOMX_CREDENTIAL_";
    private readonly StreamReader reader;
    private readonly IPipeline pipeline;
    private readonly ProviderExecutionContext context;
    private readonly ILogger<ProviderExecutionPipeline>? logger;
    private readonly CancellationToken requestCancellationToken;
    private readonly char[] readBuffer = new char[4096];
    private readonly Queue<byte> output = new();
    private string pending = string.Empty;
    private bool completed;

    public PipelineRestoringStream(
        Stream source,
        IPipeline pipeline,
        ProviderExecutionContext context,
        ILogger<ProviderExecutionPipeline>? logger,
        CancellationToken requestCancellationToken)
    {
        reader = new StreamReader(
            source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        this.pipeline = pipeline;
        this.context = context;
        this.logger = logger;
        this.requestCancellationToken = requestCancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            cancellationToken);
        while (output.Count == 0 && !completed)
            await FillOutputAsync(linked.Token);

        var length = Math.Min(buffer.Length, output.Count);
        for (var index = 0; index < length; index++)
            buffer.Span[index] = output.Dequeue();
        return length;
    }

    private async Task FillOutputAsync(CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(readBuffer.AsMemory(), cancellationToken);
        if (read == 0)
        {
            completed = true;
            await ProcessAsync(pending, cancellationToken);
            pending = string.Empty;
            return;
        }

        pending += new string(readBuffer, 0, read);
        var markerIndex = pending.LastIndexOf(PlaceholderMarker, StringComparison.Ordinal);
        var processLength = markerIndex >= 0 && pending.IndexOf("}}", markerIndex, StringComparison.Ordinal) < 0
            ? markerIndex
            : pending.Length - GetPartialMarkerSuffixLength(pending);
        if (processLength <= 0) return;

        var process = pending[..processLength];
        pending = pending[processLength..];
        await ProcessAsync(process, cancellationToken);
    }

    private static int GetPartialMarkerSuffixLength(string value)
    {
        var max = Math.Min(value.Length, PlaceholderMarker.Length - 1);
        for (var length = max; length > 0; length--)
            if (PlaceholderMarker.StartsWith(value[^length..], StringComparison.Ordinal)) return length;
        return 0;
    }

    private async Task ProcessAsync(string payload, CancellationToken cancellationToken)
    {
        if (payload.Length == 0) return;
        PipelineResult result;
        try
        {
            result = await pipeline.ExecuteAsync(payload, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Router 流式 Response Pipeline 执行失败，已阻止返回 {ProviderId}/{ModelId} {Path}",
                context.ProviderId,
                context.ModelId,
                context.UpstreamPath);
            throw new InvalidOperationException("Router 流式响应未完成本地凭据恢复，已阻止返回。", exception);
        }

        if (result.Outcome == PipelineOutcome.Blocked)
            throw new InvalidOperationException("Router 流式响应未通过本地凭据恢复，已阻止返回。");

        foreach (var item in Encoding.UTF8.GetBytes(result.Payload))
            output.Enqueue(item);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) reader.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        reader.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>网关与助手共享的 OpenAI finish_reason 规范化。</summary>
public static class ProviderResponseNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryNormalizeOpenAiFinishReasons(
        byte[] body,
        string? mediaType,
        out byte[] normalizedBody)
    {
        normalizedBody = body;
        if (body.Length == 0
            || (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var original = Encoding.UTF8.GetString(body);
        var normalized = string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            ? NormalizeSse(original)
            : NormalizeJson(original);
        if (normalized is null || string.Equals(original, normalized, StringComparison.Ordinal)) return false;

        normalizedBody = Encoding.UTF8.GetBytes(normalized);
        return true;
    }

    private static string? NormalizeSse(string body)
    {
        var lines = body.Split('\n');
        var changed = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineEnding = line.EndsWith('\r') ? "\r" : string.Empty;
            var content = lineEnding.Length == 0 ? line : line[..^1];
            if (!content.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            var payload = content[5..].Trim();
            if (payload.Length == 0 || string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase)) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch (JsonException) { continue; }

            if (!NormalizeNode(node)) continue;
            var leadingWhitespaceLength = content[5..].Length - content[5..].TrimStart().Length;
            lines[index] = $"{content[..5]}{content.Substring(5, leadingWhitespaceLength)}{node!.ToJsonString(JsonOptions)}{lineEnding}";
            changed = true;
        }

        return changed ? string.Join('\n', lines) : null;
    }

    private static string? NormalizeJson(string body)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return null; }
        return NormalizeNode(node) ? node!.ToJsonString(JsonOptions) : null;
    }

    private static bool NormalizeNode(JsonNode? node)
    {
        if (node is not JsonObject response || response["choices"] is not JsonArray choices) return false;

        var changed = false;
        foreach (var choiceNode in choices)
        {
            if (choiceNode is not JsonObject choice
                || choice["finish_reason"] is not JsonValue finishReason
                || !finishReason.TryGetValue<string>(out var value)
                || value.Length != 0)
            {
                continue;
            }

            choice["finish_reason"] = null;
            changed = true;
        }

        return changed;
    }
}
