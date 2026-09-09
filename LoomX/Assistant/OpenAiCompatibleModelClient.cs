using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// OpenAI 兼容 chat/completions 流式客户端，支持 tool calling。
/// 面向任意 OpenAI Compatible 服务（含 LoomX 自身网关）。
/// </summary>
public sealed class OpenAiCompatibleModelClient : IModelClient
{
    private readonly HttpClient httpClient;
    private readonly string baseUrl;
    private readonly string model;
    private readonly string? apiKey;
    private readonly IReadOnlyDictionary<string, string>? extraHeaders;
    private readonly string? reasoningEffort;
    private readonly ILogger<OpenAiCompatibleModelClient>? logger;

    public OpenAiCompatibleModelClient(
        HttpClient httpClient,
        string baseUrl,
        string model,
        string? apiKey = null,
        ILogger<OpenAiCompatibleModelClient>? logger = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        string? reasoningEffort = null)
    {
        this.httpClient = httpClient;
        this.baseUrl = baseUrl.TrimEnd('/');
        this.model = model;
        this.apiKey = apiKey;
        this.logger = logger;
        this.extraHeaders = extraHeaders;
        this.reasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort.Trim();
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(BuildPayload(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        ApplyExtraHeaders(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "小助手模型连接失败 {BaseUrl}", baseUrl);
            throw new ModelClientException(
                "无法连接模型服务。",
                ModelErrorKind.ConnectionFailed,
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var (errorCode, upstreamMessage) = await ReadErrorBodyAsync(response, cancellationToken);
                var kind = ModelErrorClassifier.Classify(statusCode, errorCode);
                logger?.LogWarning(
                    "小助手模型服务返回错误 {BaseUrl} {StatusCode} {ErrorCode} {Kind}",
                    baseUrl, statusCode, errorCode ?? "-", kind);
                throw new ModelClientException(
                    $"模型服务返回错误状态 {statusCode}。",
                    kind,
                    statusCode,
                    errorCode,
                    upstreamMessage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
            // 按 tool_call_id 去重：上游（如 sensenova）有时会把同一批 tool_call 扇出成多份，
            // 每份都带相同的 id 但 index 递增；按 index 累积会把同一 id 复制成 N 份进而把历史撑爆、
            // 下一次请求违反 OpenAI 协议的 "同 assistant 消息内 tool_call_id 唯一" 约束并触发 400。
            // 一旦某 builder 的 Id 已知，后续若再出现同 Id 的 delta，应合并到该 builder（而不是新建）。
            var idToBuilder = new Dictionary<string, ToolCallBuilder>(StringComparer.Ordinal);

            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line["data:".Length..].Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]") break;

                JsonNode? chunk;
                try
                {
                    chunk = JsonNode.Parse(data);
                }
                catch (JsonException)
                {
                    continue;
                }

                // 兼容部分服务在非流式/异常时返回 {"choices":[]} 或缺失 choices 的情况：
                // 空数组下 ?[0] 会抛 ArgumentOutOfRangeException，这里显式判空集合兜底为 null。
                var choices = chunk?["choices"] as JsonArray;
                var choice = choices is { Count: > 0 } ? choices[0] : null;
                var delta = choice?["delta"];
                if (delta?["content"]?.GetValue<string>() is { Length: > 0 } content)
                {
                    yield return new TextDeltaEvent(content);
                }

                if (delta?["tool_calls"] is JsonArray toolCallDeltas)
                {
                    foreach (var deltaNode in toolCallDeltas)
                    {
                        if (deltaNode is null) continue;
                        var deltaId = deltaNode["id"]?.GetValue<string>();
                        var function = deltaNode["function"];
                        var deltaName = function?["name"]?.GetValue<string>();
                        var index = deltaNode["index"]?.GetValue<int>() ?? 0;

                        // 根因防御：合法的 tool_call 首个 chunk 一定携带 id 或 name；
                        // 而后续的 arguments 分片虽然不带 id/name，但对应 index 已有 builder。
                        // 部分上游（如 sensenova）会在流里附带「既无 id/name、index 也无对应 builder」的
                        // 畸形占位片段，若为其新建 builder 会累积出 name 为空的调用，进而被误判为
                        // 「未注册工具」并以空 tool_call_id 回传历史导致 400。这里直接丢弃。
                        var hasBuilder = toolCallBuilders.ContainsKey(index);

                        if (!hasBuilder && string.IsNullOrEmpty(deltaId) && string.IsNullOrWhiteSpace(deltaName))
                        {
                            continue;
                        }

                        // 去重合并：若该 delta 带 id 且之前已见过同 id 的 builder，
                        // 把 delta 追加到已有 builder（其 index 由首次出现决定），
                        // 避免上游扇出造成同 id 多份副本。
                        ToolCallBuilder? builder = null;
                        if (deltaId is { Length: > 0 } && idToBuilder.TryGetValue(deltaId, out var existing))
                        {
                            builder = existing;
                        }

                        if (builder is null && !hasBuilder)
                        {
                            builder = new ToolCallBuilder();
                            toolCallBuilders[index] = builder;
                            if (deltaId is { Length: > 0 })
                            {
                                idToBuilder[deltaId] = builder;
                            }
                        }
                        else if (builder is null)
                        {
                            builder = toolCallBuilders[index];
                        }

                        if (deltaId is { Length: > 0 } && !idToBuilder.ContainsKey(deltaId))
                        {
                            idToBuilder[deltaId] = builder;
                        }
                        if (deltaId is { Length: > 0 }) builder.Id = deltaId;
                        if (deltaName is { Length: > 0 }) builder.Name = deltaName;
                        if (function?["arguments"]?.GetValue<string>() is { } arguments) builder.Arguments.Append(arguments);
                    }
                }

                if (choice?["finish_reason"]?.GetValue<string>() is { } finishReason)
                {
                    foreach (var entry in toolCallBuilders.OrderBy(item => item.Key))
                    {
                        var toolCall = entry.Value.ToToolCall();
                        // 兜底保险：正常路径已在解析时丢弃无 id/name 的占位片段，
                        // 此处再过滤一次 name 为空的调用，双保险防止无效调用透传到 AgentLoop。
                        if (string.IsNullOrWhiteSpace(toolCall.Name)) continue;
                        yield return new ModelToolCallEvent(toolCall);
                    }
                    yield return new ModelCompletedEvent(finishReason);
                    // 同一流里若出现多个 finish_reason chunk（如上游在重试/异常后再次发送完成帧），
                    // 清空累积器避免重复发射同一组 tool_call。
                    toolCallBuilders.Clear();
                    idToBuilder.Clear();
                }
            }
        }
    }

    private JsonObject BuildPayload(ModelRequest request)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            var item = new JsonObject
            {
                ["role"] = message.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    ChatRole.Assistant => "assistant",
                    ChatRole.Tool => "tool",
                    _ => "user",
                },
            };
            if (message.Content is not null) item["content"] = message.Content;
            if (message.ToolCalls.Count > 0)
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.ArgumentsJson,
                        },
                    });
                }
                item["tool_calls"] = calls;
            }
            if (message.Role == ChatRole.Tool && message.ToolCallId is not null) item["tool_call_id"] = message.ToolCallId;
            messages.Add(item);
        }

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
        };
        if (reasoningEffort is not null) payload["reasoning_effort"] = reasoningEffort;

        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = tool.ParametersSchema.DeepClone(),
                    },
                });
            }
            payload["tools"] = tools;
        }

        return payload;
    }

    /// <summary>
    /// 读取并解析错误响应体（限量 <see cref="ModelErrorClassifier.MaxErrorBodyBytes"/>），
    /// 返回 Provider 错误码与脱敏后的上游描述。读取失败静默降级为 null，不掩盖原始状态码。
    /// </summary>
    private static async Task<(string? ErrorCode, string? UpstreamMessage)> ReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[ModelErrorClassifier.MaxErrorBodyBytes];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
                if (read == 0) break;
                totalRead += read;
            }

            var body = Encoding.UTF8.GetString(buffer, 0, totalRead);
            var (code, message) = ModelErrorClassifier.ParseErrorBody(body);
            return (code, ModelErrorClassifier.SanitizeUpstreamMessage(message));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// 注入 Provider/模型配置的自定义头（User-Agent、X-* 等）。
    /// Authorization 与 Content-Type 由客户端自身管理，跳过避免冲突。
    /// </summary>
    private void ApplyExtraHeaders(HttpRequestMessage httpRequest)
    {
        if (extraHeaders is null) return;
        foreach (var header in extraHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value)) continue;
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private sealed class ToolCallBuilder
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public readonly StringBuilder Arguments = new();

        public ToolCall ToToolCall() => new(Id, Name, Arguments.ToString());
    }
}
