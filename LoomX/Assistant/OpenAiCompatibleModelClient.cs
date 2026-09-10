using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Services;
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
    private readonly bool useResponsesEndpoint;
    private readonly bool useCodexIdentity;
    private readonly string codexSessionId = Guid.NewGuid().ToString("D");
    private readonly string codexRootTurnId = Guid.NewGuid().ToString("D");
    private readonly IProviderExecutionPipeline executionPipeline;
    private readonly string providerId;

    public OpenAiCompatibleModelClient(
        HttpClient httpClient,
        string baseUrl,
        string model,
        string? apiKey = null,
        ILogger<OpenAiCompatibleModelClient>? logger = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        string? reasoningEffort = null,
        string? endpointFormat = "chat_completions",
        IProviderExecutionPipeline? executionPipeline = null,
        string? providerId = null)
    {
        this.httpClient = httpClient;
        this.baseUrl = baseUrl.TrimEnd('/');
        this.model = model;
        this.apiKey = apiKey;
        this.logger = logger;
        this.extraHeaders = extraHeaders;
        this.reasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort.Trim();
        useResponsesEndpoint = string.Equals(endpointFormat, "responses", StringComparison.OrdinalIgnoreCase);
        useCodexIdentity = useResponsesEndpoint && HasCodexIdentity(extraHeaders);
        this.executionPipeline = executionPipeline ?? new ProviderExecutionPipeline();
        this.providerId = string.IsNullOrWhiteSpace(providerId) ? "assistant" : providerId.Trim();
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolNames = useResponsesEndpoint
            ? BuildResponsesToolNameMap(request.Tools)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var wireToOriginalToolNames = toolNames.ToDictionary(
            item => item.Value,
            item => item.Key,
            StringComparer.Ordinal);
        var chatPayload = BuildPayload(request, toolNames);
        var payload = useResponsesEndpoint
            ? OpenAiResponsesBridge.CreateResponsesRequest(chatPayload)
            : chatPayload;
        var codexTurnId = useCodexIdentity ? Guid.NewGuid().ToString("D") : null;
        if (codexTurnId is not null)
        {
            ApplyCodexRequestShape(payload, codexTurnId);
        }
        var endpoint = useResponsesEndpoint ? "/responses" : "/chat/completions";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{endpoint}")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (useResponsesEndpoint)
        {
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        ApplyExtraHeaders(httpRequest);
        if (useCodexIdentity)
        {
            ApplyCodexRequestHeaders(httpRequest);
        }

        ProviderExecutionResult result;
        try
        {
            result = await executionPipeline.ExecuteAsync(
                httpClient,
                httpRequest,
                new ProviderExecutionContext(providerId, model, "openai", endpoint),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "小助手模型连接失败 {BaseUrl}", baseUrl);
            throw new ModelClientException(
                "无法连接模型服务。",
                ModelErrorKind.ConnectionFailed,
                innerException: exception);
        }

        if (!result.IsSuccess)
        {
            var statusCode = (int)result.StatusCode;
            var (errorCode, upstreamMessage) = ReadErrorBody(result.Body);
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

        using var reader = new StringReader(result.BodyText);
        if (useResponsesEndpoint)
        {
            var responsesBody = await reader.ReadToEndAsync(cancellationToken);
            var chatSse = ConvertResponsesBody(responsesBody, result.ContentType);
            using var translatedReader = new StringReader(chatSse);
            await foreach (var streamEvent in ParseChatCompletionsSseAsync(translatedReader, cancellationToken))
            {
                yield return RestoreToolName(streamEvent, wireToOriginalToolNames);
            }

            yield break;
        }

        await foreach (var streamEvent in ParseChatCompletionsBodyAsync(reader, cancellationToken))
        {
            yield return RestoreToolName(streamEvent, wireToOriginalToolNames);
        }
    }

    private string ConvertResponsesBody(string body, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ModelClientException(
                "模型服务返回空响应。",
                ModelErrorKind.Unknown,
                upstreamMessage: "模型响应无效。请检查 Provider 协议与账户额度。");
        }

        try
        {
            var trimmedBody = body.TrimStart();
            var isSse = trimmedBody.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || trimmedBody.StartsWith("event:", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
                    && !trimmedBody.StartsWith("{", StringComparison.Ordinal)
                    && !trimmedBody.StartsWith("[", StringComparison.Ordinal));
            if (isSse)
            {
                var chatSse = OpenAiResponsesBridge.CreateChatCompletionsSse(body, model);
                if (!chatSse.Contains("\"content\":", StringComparison.Ordinal)
                    && !chatSse.Contains("\"tool_calls\":", StringComparison.Ordinal))
                {
                    throw InvalidResponsesException("模型响应没有文本或工具调用。");
                }

                return chatSse;
            }

            if (JsonNode.Parse(body) is JsonObject responsesResponse)
            {
                ThrowIfStructuredError(body);
                if (responsesResponse["output"] is not JsonArray { Count: > 0 })
                {
                    throw InvalidResponsesException("模型响应没有输出内容。");
                }

                var chatResponse = OpenAiResponsesBridge.CreateChatCompletionsResponse(responsesResponse, model);
                var choice = chatResponse["choices"]?[0];
                var message = choice?["message"];
                if (message?["content"] is null && message?["tool_calls"] is not JsonArray { Count: > 0 })
                {
                    throw InvalidResponsesException("模型响应没有文本或工具调用。");
                }

                return $"data: {chatResponse.ToJsonString()}\n\ndata: [DONE]\n\n";
            }
        }
        catch (ModelClientException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            logger?.LogWarning(exception, "小助手模型 Responses 响应无法转换 {BaseUrl}", baseUrl);
        }

        throw new ModelClientException(
            "模型服务返回无效响应。",
            ModelErrorKind.Unknown,
            upstreamMessage: "模型响应格式无效。请检查 Provider 的 Responses API 配置。");
    }

    private static void ThrowIfStructuredError(string body)
    {
        var (errorCode, upstreamMessage) = ModelErrorClassifier.ParseErrorBody(body);
        if (string.IsNullOrWhiteSpace(errorCode) && string.IsNullOrWhiteSpace(upstreamMessage)) return;

        var kind = ModelErrorClassifier.Classify(null, errorCode);
        throw new ModelClientException(
            "模型服务返回错误响应。",
            kind,
            errorCode: errorCode,
            upstreamMessage: ModelErrorClassifier.SanitizeUpstreamMessage(upstreamMessage));
    }

    private static ModelClientException InvalidResponsesException(string detail, Exception? innerException = null) => new(
        "模型服务返回无效响应。",
        ModelErrorKind.Unknown,
        innerException: innerException,
        upstreamMessage: detail);

    private static async IAsyncEnumerable<ModelStreamEvent> ParseChatCompletionsBodyAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? firstLine;
        do
        {
            firstLine = await reader.ReadLineAsync(cancellationToken);
        }
        while (firstLine is not null && string.IsNullOrWhiteSpace(firstLine));

        if (firstLine is null)
        {
            throw new ModelClientException(
                "模型服务返回空响应。",
                ModelErrorKind.Unknown,
                upstreamMessage: "模型响应无效。请检查 Provider 协议与账户额度。");
        }

        var trimmed = firstLine.TrimStart();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("event:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(":", StringComparison.Ordinal))
        {
            await foreach (var streamEvent in ParseChatCompletionsSseAsync(
                new PrefixedTextReader(firstLine, reader),
                cancellationToken))
            {
                yield return streamEvent;
            }

            yield break;
        }

        var body = new StringBuilder(firstLine);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            body.AppendLine(line);
        }

        // 部分中转站会忽略 stream=true 并返回完整 JSON；将其转换为统一的模型事件。
        var bodyText = body.ToString();
        ThrowIfStructuredError(bodyText);
        JsonNode? response;
        try
        {
            response = JsonNode.Parse(bodyText);
        }
        catch (JsonException exception)
        {
            throw InvalidResponsesException("模型响应格式无效。请检查 Provider 的 Chat Completions API 配置。", exception);
        }

        if (response is not JsonObject responseObject)
        {
            throw InvalidResponsesException("模型响应格式无效。请检查 Provider 的 Chat Completions API 配置。");
        }

        foreach (var streamEvent in ParseChatCompletionsJson(responseObject))
        {
            yield return streamEvent;
        }
    }

    private static IEnumerable<ModelStreamEvent> ParseChatCompletionsJson(JsonObject response)
    {
        var choices = response["choices"] as JsonArray;
        var choice = choices is { Count: > 0 } ? choices[0] as JsonObject : null;
        if (choice is null)
        {
            throw InvalidResponsesException("模型响应没有 choices。请检查 Provider 的 Chat Completions API 配置。");
        }

        var message = choice["message"] as JsonObject;
        if (message?["content"]?.GetValue<string>() is { Length: > 0 } content)
        {
            yield return new TextDeltaEvent(content);
        }

        var toolCalls = message?["tool_calls"] as JsonArray;
        if (toolCalls is not null)
        {
            foreach (var toolCallNode in toolCalls.OfType<JsonObject>())
            {
                var function = toolCallNode["function"] as JsonObject;
                var name = function?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;

                yield return new ModelToolCallEvent(new ToolCall(
                    toolCallNode["id"]?.GetValue<string>() ?? string.Empty,
                    name,
                    function?["arguments"]?.GetValue<string>() ?? "{}"));
            }
        }

        var finishReason = choice["finish_reason"]?.GetValue<string>();
        yield return new ModelCompletedEvent(
            string.IsNullOrWhiteSpace(finishReason)
                ? toolCalls is { Count: > 0 } ? "tool_calls" : "stop"
                : finishReason);
    }

    private static async IAsyncEnumerable<ModelStreamEvent> ParseChatCompletionsSseAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
        // 按 tool_call_id 去重：上游（如 sensenova）有时会把同一批 tool_call 扇出成多份，
        // 每份都带相同的 id 但 index 递增；按 index 累积会把同一 id 复制成 N 份进而把历史撑爆、
        // 下一次请求违反 OpenAI 协议的 "同 assistant 消息内 tool_call_id 唯一" 约束并触发 400。
        // 一旦某 builder 的 Id 已知，后续若再出现同 Id 的 delta，应合并到该 builder（而不是新建）。
        var idToBuilder = new Dictionary<string, ToolCallBuilder>(StringComparer.Ordinal);
        var completionEmitted = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]")
            {
                if (completionEmitted)
                {
                    break;
                }

                var toolCalls = DrainToolCalls(toolCallBuilders, idToBuilder);
                foreach (var toolCall in toolCalls)
                {
                    yield return new ModelToolCallEvent(toolCall);
                }

                if (toolCalls.Length > 0)
                {
                    yield return new ModelCompletedEvent("tool_calls");
                    completionEmitted = true;
                }

                break;
            }

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

            // 某些兼容端点即使请求 stream=true 仍返回完整 chat.completion，
            // 工具调用位于 choice.message.tool_calls 而不是 delta.tool_calls。
            if (delta is null && choice?["message"] is JsonObject message)
            {
                if (message["content"]?.GetValue<string>() is { Length: > 0 } messageContent)
                {
                    yield return new TextDeltaEvent(messageContent);
                }

                if (message["tool_calls"] is JsonArray messageToolCalls)
                {
                    foreach (var toolCallNode in messageToolCalls.OfType<JsonObject>())
                    {
                        var function = toolCallNode["function"] as JsonObject;
                        var name = function?["name"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        yield return new ModelToolCallEvent(new ToolCall(
                            toolCallNode["id"]?.GetValue<string>() ?? string.Empty,
                            name,
                            function?["arguments"]?.GetValue<string>() ?? "{}"));
                    }
                }
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
                    if (function?["arguments"]?.GetValue<string>() is { } arguments) builder.AppendArguments(arguments);
                }
            }

            if (choice?["finish_reason"]?.GetValue<string>() is { } finishReason
                && !string.IsNullOrWhiteSpace(finishReason))
            {
                foreach (var toolCall in DrainToolCalls(toolCallBuilders, idToBuilder))
                {
                    yield return new ModelToolCallEvent(toolCall);
                }
                yield return new ModelCompletedEvent(finishReason);
                completionEmitted = true;
            }
        }

        // 一些兼容端点只发送 [DONE] 之前的增量后直接关闭连接，
        // 没有 finish_reason，也可能省略 [DONE]。结束时仍需把完整工具调用交给 AgentLoop。
        if (!completionEmitted && toolCallBuilders.Count > 0)
        {
            var toolCalls = DrainToolCalls(toolCallBuilders, idToBuilder);
            foreach (var toolCall in toolCalls)
            {
                yield return new ModelToolCallEvent(toolCall);
            }

            if (toolCalls.Length > 0)
            {
                yield return new ModelCompletedEvent("tool_calls");
            }
        }
    }

    private static ToolCall[] DrainToolCalls(
        IDictionary<int, ToolCallBuilder> toolCallBuilders,
        IDictionary<string, ToolCallBuilder> idToBuilder)
    {
        var toolCalls = toolCallBuilders
            .OrderBy(item => item.Key)
            .Select(item => item.Value.ToToolCall())
            .Where(toolCall => !string.IsNullOrWhiteSpace(toolCall.Name))
            .ToArray();

        // 同一流里若出现多个 finish_reason chunk（如上游在重试/异常后再次发送完成帧），
        // 清空累积器避免重复发射同一组 tool_call。
        toolCallBuilders.Clear();
        idToBuilder.Clear();
        return toolCalls;
    }

    private JsonObject BuildPayload(
        ModelRequest request,
        IReadOnlyDictionary<string, string> toolNames)
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
                            ["name"] = ResolveWireToolName(call.Name, toolNames),
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
        var effectiveReasoningEffort = reasoningEffort ?? (useCodexIdentity ? "low" : null);
        if (effectiveReasoningEffort is not null) payload["reasoning_effort"] = effectiveReasoningEffort;

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
                        ["name"] = ResolveWireToolName(tool.Name, toolNames),
                        ["description"] = tool.Description,
                        ["parameters"] = tool.ParametersSchema.DeepClone(),
                    },
                });
            }
            payload["tools"] = tools;
        }

        return payload;
    }

    private static IReadOnlyDictionary<string, string> BuildResponsesToolNameMap(
        IReadOnlyCollection<ToolDefinition> tools)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var normalized = new string(tool.Name.Select(character =>
                character is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_' or '-'
                    ? character
                    : '_').ToArray());
            if (string.IsNullOrWhiteSpace(normalized)) normalized = "tool";
            if (normalized.Length > 64) normalized = normalized[..64];

            var candidate = normalized;
            for (var suffix = 2; !usedNames.Add(candidate); suffix++)
            {
                var suffixText = $"_{suffix}";
                candidate = normalized[..Math.Min(normalized.Length, 64 - suffixText.Length)] + suffixText;
            }

            result[tool.Name] = candidate;
        }

        return result;
    }

    private static string ResolveWireToolName(
        string toolName,
        IReadOnlyDictionary<string, string> toolNames) =>
        toolNames.TryGetValue(toolName, out var wireName) ? wireName : toolName;

    private static ModelStreamEvent RestoreToolName(
        ModelStreamEvent streamEvent,
        IReadOnlyDictionary<string, string> wireToOriginalToolNames)
    {
        if (streamEvent is not ModelToolCallEvent toolCallEvent
            || !wireToOriginalToolNames.TryGetValue(toolCallEvent.ToolCall.Name, out var originalName))
        {
            return streamEvent;
        }

        return new ModelToolCallEvent(toolCallEvent.ToolCall with { Name = originalName });
    }

    private void ApplyCodexRequestShape(JsonObject payload, string turnId)
    {
        var sessionId = ResolveCodexSessionId();
        if (payload["prompt_cache_key"] is null)
        {
            payload["prompt_cache_key"] = sessionId;
        }

        var metadata = payload["client_metadata"] as JsonObject;
        if (metadata is null)
        {
            metadata = new JsonObject();
            payload["client_metadata"] = metadata;
        }
        metadata["session_id"] ??= sessionId;
        metadata["thread_id"] ??= sessionId;
        metadata["root_turn_id"] ??= codexRootTurnId;
        metadata["turn_id"] ??= turnId;

        if (payload["tools"] is not JsonArray { Count: > 0 } tools)
        {
            return;
        }

        foreach (var tool in tools.OfType<JsonObject>())
        {
            if (string.Equals(tool["type"]?.GetValue<string>(), "function", StringComparison.OrdinalIgnoreCase))
            {
                tool["strict"] ??= false;
            }
        }
        payload["parallel_tool_calls"] ??= true;
        payload["tool_choice"] ??= "auto";
    }

    private void ApplyCodexRequestHeaders(HttpRequestMessage httpRequest)
    {
        var sessionId = ResolveCodexSessionId();
        AddHeaderIfMissing(httpRequest, "session-id", sessionId);
        AddHeaderIfMissing(httpRequest, "thread-id", sessionId);
        AddHeaderIfMissing(httpRequest, "x-client-request-id", sessionId);
    }

    private string ResolveCodexSessionId()
    {
        if (extraHeaders is not null)
        {
            foreach (var headerName in new[] { "session-id", "session_id" })
            {
                var configured = extraHeaders.FirstOrDefault(header =>
                    header.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(configured.Value))
                {
                    return configured.Value.Trim();
                }
            }
        }

        return codexSessionId;
    }

    private static void AddHeaderIfMissing(HttpRequestMessage httpRequest, string name, string value)
    {
        if (!httpRequest.Headers.Contains(name))
        {
            httpRequest.Headers.TryAddWithoutValidation(name, value);
        }
    }

    /// <summary>从统一执行结果中解析错误响应体，正文仅用于本地分类且不会写入日志。</summary>
    private static (string? ErrorCode, string? UpstreamMessage) ReadErrorBody(byte[] body)
    {
        var limited = body.Length <= ModelErrorClassifier.MaxErrorBodyBytes
            ? body
            : body[..ModelErrorClassifier.MaxErrorBodyBytes];
        var text = Encoding.UTF8.GetString(limited);
        var (code, message) = ModelErrorClassifier.ParseErrorBody(text);
        return (code, ModelErrorClassifier.SanitizeUpstreamMessage(message));
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

            httpRequest.Headers.TryAddWithoutValidation(
                header.Key,
                useCodexIdentity ? NormalizeCodexIdentityHeader(header.Key, header.Value) : header.Value);
        }
    }

    private static bool HasCodexIdentity(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return false;
        foreach (var header in headers)
        {
            if (header.Key.Equals("originator", StringComparison.OrdinalIgnoreCase)
                && (header.Value.Equals("codex-cli", StringComparison.OrdinalIgnoreCase)
                    || header.Value.Equals("codex_cli_rs", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                && (header.Value.StartsWith("codex-cli/", StringComparison.OrdinalIgnoreCase)
                    || header.Value.StartsWith("codex_cli_rs/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeCodexIdentityHeader(string name, string value)
    {
        if (name.Equals("originator", StringComparison.OrdinalIgnoreCase)
            && value.Equals("codex-cli", StringComparison.OrdinalIgnoreCase))
        {
            return "codex_cli_rs";
        }

        if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
            && value.StartsWith("codex-cli/", StringComparison.OrdinalIgnoreCase))
        {
            return "codex_cli_rs/" + value["codex-cli/".Length..];
        }

        return value;
    }

    private sealed class ToolCallBuilder
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public readonly StringBuilder Arguments = new();

        public void AppendArguments(string fragment)
        {
            if (fragment.Length == 0) return;

            var current = Arguments.ToString();
            if (current.Length == 0)
            {
                Arguments.Append(fragment);
                return;
            }

            // 同一 tool_call_id 被扇出时，上游可能重复发送完整参数快照。
            // 工具参数按协议必须是 JSON object；一旦已有完整对象，后续副本不能继续拼接。
            if (IsCompleteJsonObject(current)) return;

            // 兼容累计快照：新片段已包含当前前缀时，用较完整的快照替换；
            // 反向则是旧快照重复，直接忽略。其余情况按标准增量追加。
            if (fragment.StartsWith(current, StringComparison.Ordinal))
            {
                Arguments.Clear();
                Arguments.Append(fragment);
                return;
            }
            if (current.StartsWith(fragment, StringComparison.Ordinal)) return;

            Arguments.Append(fragment);
        }

        public ToolCall ToToolCall()
        {
            var arguments = Arguments.Length == 0 ? "{}" : Arguments.ToString();
            if (!IsCompleteJsonObject(arguments))
            {
                throw InvalidResponsesException("模型工具调用参数不完整，未进入工具执行。");
            }

            return new ToolCall(Id, Name, arguments);
        }

        private static bool IsCompleteJsonObject(string value)
        {
            try
            {
                return JsonNode.Parse(value) is JsonObject;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private sealed class PrefixedTextReader(string firstLine, TextReader inner) : TextReader
    {
        private string? pendingLine = firstLine;

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (pendingLine is not null)
            {
                var line = pendingLine;
                pendingLine = null;
                return ValueTask.FromResult<string?>(line);
            }

            return inner.ReadLineAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
