using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoomX.Services;

/// <summary>
/// Provider 请求执行所需的安全上下文。请求体和密钥仍由调用方构造，管线只负责发送与收集响应。
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

public interface IProviderExecutionPipeline
{
    Task<ProviderExecutionResult> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 统一的 Provider 请求后半段：发送、读取响应、收集元数据和协议级轻量规范化。
/// 不记录请求或响应正文，调用方负责将结果映射到自己的入口协议。
/// </summary>
public sealed class ProviderExecutionPipeline : IProviderExecutionPipeline
{
    public async Task<ProviderExecutionResult> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

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

        return new ProviderExecutionResult(response.StatusCode, contentType, headers, body, normalized);
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
