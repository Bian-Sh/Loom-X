using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OllamaHub.Services;

internal static class OpenAiResponsesBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonObject CreateResponsesRequest(JsonObject chatRequest)
    {
        var responsesRequest = chatRequest.DeepClone().AsObject();
        var hasMessages = responsesRequest["messages"] is JsonArray;
        MoveProperty(responsesRequest, "messages", "input");
        MoveProperty(responsesRequest, "max_tokens", "max_output_tokens");
        MoveProperty(responsesRequest, "max_completion_tokens", "max_output_tokens");
        responsesRequest.Remove("stream_options");

        if (hasMessages && responsesRequest["input"] is JsonArray input)
        {
            responsesRequest["input"] = ConvertInput(input);
        }

        if (responsesRequest["tools"] is JsonArray tools)
        {
            var convertedTools = new JsonArray();
            foreach (var tool in tools)
            {
                convertedTools.Add(ConvertTool(tool));
            }

            responsesRequest["tools"] = convertedTools;
        }

        if (responsesRequest["tool_choice"] is JsonObject toolChoice
            && string.Equals(toolChoice["type"]?.GetValue<string>(), "function", StringComparison.OrdinalIgnoreCase)
            && toolChoice["function"] is JsonObject function)
        {
            responsesRequest["tool_choice"] = new JsonObject
            {
                ["type"] = "function",
                ["name"] = function["name"]?.DeepClone()
            };
        }

        return responsesRequest;
    }

    public static JsonObject CreateChatCompletionsResponse(JsonObject responsesResponse, string modelId)
    {
        var responseId = "chatcmpl-bridge";
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UpdateResponseMetadata(responsesResponse, ref responseId, ref createdAt);

        var content = new StringBuilder();
        var toolCalls = new JsonArray();
        if (responsesResponse["output"] is JsonArray output)
        {
            foreach (var item in output.OfType<JsonObject>())
            {
                if (string.Equals(item["type"]?.GetValue<string>(), "message", StringComparison.OrdinalIgnoreCase))
                {
                    AppendOutputText(item["content"] as JsonArray, content);
                    continue;
                }

                if (!string.Equals(item["type"]?.GetValue<string>(), "function_call", StringComparison.OrdinalIgnoreCase)
                    || item["name"]?.GetValue<string>() is not { Length: > 0 } functionName)
                {
                    continue;
                }

                toolCalls.Add(new JsonObject
                {
                    ["id"] = item["call_id"]?.GetValue<string>() ?? item["id"]?.GetValue<string>() ?? "call_bridge",
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = functionName,
                        ["arguments"] = item["arguments"]?.GetValue<string>() ?? "{}"
                    }
                });
            }
        }

        var message = new JsonObject { ["role"] = "assistant" };
        if (content.Length > 0)
        {
            message["content"] = content.ToString();
        }

        if (toolCalls.Count > 0)
        {
            message["tool_calls"] = toolCalls;
        }

        return new JsonObject
        {
            ["id"] = responseId,
            ["object"] = "chat.completion",
            ["created"] = createdAt,
            ["model"] = modelId,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = message,
                    ["finish_reason"] = toolCalls.Count > 0 ? "tool_calls" : "stop"
                }
            }
        };
    }

    public static string CreateChatCompletionsSse(string responsesSse, string modelId)
    {
        var output = new StringBuilder();
        var responseId = "chatcmpl-bridge";
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var emittedChunk = false;
        var usedToolCalls = false;
        var toolCalls = new Dictionary<string, ToolCallState>(StringComparer.Ordinal);
        var eventData = new List<string>();

        void WriteChunk(JsonObject delta, string? finishReason = null)
        {
            var chunk = new JsonObject
            {
                ["id"] = responseId,
                ["object"] = "chat.completion.chunk",
                ["created"] = createdAt,
                ["model"] = modelId,
                ["choices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = delta,
                        ["finish_reason"] = finishReason
                    }
                }
            };
            output.Append("data: ").Append(chunk.ToJsonString(JsonOptions)).Append("\n\n");
            emittedChunk = true;
        }

        void ProcessEvent()
        {
            if (eventData.Count == 0)
            {
                return;
            }

            var data = string.Join("\n", eventData);
            eventData.Clear();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            JsonObject? payload;
            try
            {
                payload = JsonNode.Parse(data) as JsonObject;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Responses 流包含无法解析的事件。", exception);
            }

            if (payload is null)
            {
                throw new InvalidDataException("Responses 流事件必须是 JSON 对象。");
            }

            var eventType = payload["type"]?.GetValue<string>();
            switch (eventType)
            {
                case "response.created":
                    UpdateResponseMetadata(payload["response"] as JsonObject, ref responseId, ref createdAt);
                    break;

                case "response.output_item.added":
                    var item = payload["item"] as JsonObject;
                    var itemType = item?["type"]?.GetValue<string>();
                    if (string.Equals(itemType, "message", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteChunk(new JsonObject { ["role"] = "assistant" });
                    }
                    else if (string.Equals(itemType, "function_call", StringComparison.OrdinalIgnoreCase)
                        && item?["id"]?.GetValue<string>() is { Length: > 0 } itemId
                        && item["name"]?.GetValue<string>() is { Length: > 0 } functionName)
                    {
                        var callId = item["call_id"]?.GetValue<string>() ?? itemId;
                        var state = new ToolCallState(toolCalls.Count, callId);
                        toolCalls[itemId] = state;
                        usedToolCalls = true;
                        WriteChunk(new JsonObject
                        {
                            ["tool_calls"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["index"] = state.Index,
                                    ["id"] = state.CallId,
                                    ["type"] = "function",
                                    ["function"] = new JsonObject { ["name"] = functionName }
                                }
                            }
                        });
                    }

                    break;

                case "response.output_text.delta":
                    if (payload["delta"]?.GetValue<string>() is { Length: > 0 } textDelta)
                    {
                        WriteChunk(new JsonObject { ["content"] = textDelta });
                    }

                    break;

                case "response.function_call_arguments.delta":
                    if (payload["item_id"]?.GetValue<string>() is { Length: > 0 } functionItemId
                        && toolCalls.TryGetValue(functionItemId, out var toolCall)
                        && payload["delta"]?.GetValue<string>() is { Length: > 0 } argumentsDelta)
                    {
                        WriteChunk(new JsonObject
                        {
                            ["tool_calls"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["index"] = toolCall.Index,
                                    ["function"] = new JsonObject { ["arguments"] = argumentsDelta }
                                }
                            }
                        });
                    }

                    break;

                case "response.completed":
                    UpdateResponseMetadata(payload["response"] as JsonObject, ref responseId, ref createdAt);
                    WriteChunk(new JsonObject(), usedToolCalls ? "tool_calls" : "stop");
                    output.Append("data: [DONE]\n\n");
                    break;

                case "error":
                    throw new InvalidDataException("Responses 流返回错误事件。");
            }
        }

        using var reader = new StringReader(responsesSse);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                ProcessEvent();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                eventData.Add(line[5..].TrimStart());
            }
        }

        ProcessEvent();
        if (!emittedChunk || !output.ToString().EndsWith("data: [DONE]\n\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Responses 流未包含完成事件。");
        }

        return output.ToString();
    }

    private static JsonNode? ConvertTool(JsonNode? tool)
    {
        if (tool is not JsonObject source
            || !string.Equals(source["type"]?.GetValue<string>(), "function", StringComparison.OrdinalIgnoreCase)
            || source["function"] is not JsonObject function)
        {
            return tool?.DeepClone();
        }

        var converted = new JsonObject
        {
            ["type"] = "function",
            ["name"] = function["name"]?.DeepClone()
        };
        CopyProperty(function, converted, "description");
        CopyProperty(function, converted, "parameters");
        CopyProperty(function, converted, "strict");
        return converted;
    }

    private static JsonArray ConvertInput(JsonArray messages)
    {
        var converted = new JsonArray();
        foreach (var messageNode in messages)
        {
            if (messageNode is not JsonObject message)
            {
                converted.Add(messageNode?.DeepClone());
                continue;
            }

            var role = message["role"]?.GetValue<string>();
            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                converted.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message["tool_call_id"]?.DeepClone() ?? message["call_id"]?.DeepClone() ?? "call_bridge",
                    ["output"] = ToText(message["content"])
                });
                continue;
            }

            var toolCalls = message["tool_calls"] as JsonArray;
            var responseMessage = message.DeepClone().AsObject();
            responseMessage.Remove("tool_calls");
            ConvertMessageContent(responseMessage);
            if (responseMessage["content"] is not null || toolCalls is null || toolCalls.Count == 0)
            {
                converted.Add(responseMessage);
            }

            if (toolCalls is null)
            {
                continue;
            }

            foreach (var toolCallNode in toolCalls.OfType<JsonObject>())
            {
                if (toolCallNode["function"] is not JsonObject function
                    || function["name"]?.GetValue<string>() is not { Length: > 0 } functionName)
                {
                    continue;
                }

                converted.Add(new JsonObject
                {
                    ["type"] = "function_call",
                    ["call_id"] = toolCallNode["id"]?.DeepClone() ?? "call_bridge",
                    ["name"] = functionName,
                    ["arguments"] = function["arguments"]?.DeepClone() ?? "{}"
                });
            }
        }

        return converted;
    }

    private static void ConvertMessageContent(JsonObject message)
    {
        if (message["content"] is not JsonArray content)
        {
            return;
        }

        var converted = new JsonArray();
        foreach (var partNode in content)
        {
            if (partNode is not JsonObject part)
            {
                converted.Add(partNode?.DeepClone());
                continue;
            }

            var type = part["type"]?.GetValue<string>();
            if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
            {
                part["type"] = "input_text";
            }
            else if (string.Equals(type, "image_url", StringComparison.OrdinalIgnoreCase))
            {
                part["type"] = "input_image";
                if (part["image_url"] is JsonObject imageUrl && imageUrl["url"] is { } url)
                {
                    part["image_url"] = url.DeepClone();
                    if (imageUrl["detail"] is { } detail)
                    {
                        part["detail"] = detail.DeepClone();
                    }
                }
            }

            converted.Add(part.DeepClone());
        }

        message["content"] = converted;
    }

    private static string ToText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (content is JsonArray parts)
        {
            var output = new StringBuilder();
            foreach (var part in parts.OfType<JsonObject>())
            {
                if (part["text"]?.GetValue<string>() is { } partText)
                {
                    output.Append(partText);
                }
            }

            return output.ToString();
        }

        return content?.ToJsonString() ?? string.Empty;
    }

    private static void MoveProperty(JsonObject source, string sourceName, string targetName)
    {
        if (source[sourceName] is not { } value)
        {
            return;
        }

        source.Remove(sourceName);
        if (source[targetName] is null)
        {
            source[targetName] = value;
        }
    }

    private static void CopyProperty(JsonObject source, JsonObject target, string propertyName)
    {
        if (source[propertyName] is { } value)
        {
            target[propertyName] = value.DeepClone();
        }
    }

    private static void AppendOutputText(JsonArray? contentParts, StringBuilder output)
    {
        if (contentParts is null)
        {
            return;
        }

        foreach (var contentPart in contentParts.OfType<JsonObject>())
        {
            if (string.Equals(contentPart["type"]?.GetValue<string>(), "output_text", StringComparison.OrdinalIgnoreCase)
                && contentPart["text"]?.GetValue<string>() is { } text)
            {
                output.Append(text);
            }
        }
    }

    private static void UpdateResponseMetadata(JsonObject? response, ref string responseId, ref long createdAt)
    {
        if (response?["id"]?.GetValue<string>() is { Length: > 0 } parsedResponseId)
        {
            responseId = parsedResponseId;
        }

        if (response?["created_at"]?.GetValue<long>() is { } parsedCreatedAt)
        {
            createdAt = parsedCreatedAt;
        }
    }

    private sealed record ToolCallState(int Index, string CallId);
}
