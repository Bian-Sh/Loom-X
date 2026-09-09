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
    private readonly ILogger<OpenAiCompatibleModelClient>? logger;

    public OpenAiCompatibleModelClient(
        HttpClient httpClient,
        string baseUrl,
        string model,
        string? apiKey = null,
        ILogger<OpenAiCompatibleModelClient>? logger = null)
    {
        this.httpClient = httpClient;
        this.baseUrl = baseUrl.TrimEnd('/');
        this.model = model;
        this.apiKey = apiKey;
        this.logger = logger;
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

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "小助手模型连接失败 {BaseUrl}", baseUrl);
            throw new ModelClientException("无法连接模型服务。", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning("小助手模型服务返回错误 {BaseUrl} {StatusCode}", baseUrl, (int)response.StatusCode);
                throw new ModelClientException($"模型服务返回错误状态 {(int)response.StatusCode}。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();

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

                var choice = chunk?["choices"]?[0];
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
                        var index = deltaNode["index"]?.GetValue<int>() ?? 0;
                        if (!toolCallBuilders.TryGetValue(index, out var builder))
                        {
                            builder = new ToolCallBuilder();
                            toolCallBuilders[index] = builder;
                        }
                        if (deltaNode["id"]?.GetValue<string>() is { } id) builder.Id = id;
                        var function = deltaNode["function"];
                        if (function?["name"]?.GetValue<string>() is { } name) builder.Name = name;
                        if (function?["arguments"]?.GetValue<string>() is { } arguments) builder.Arguments.Append(arguments);
                    }
                }

                if (choice?["finish_reason"]?.GetValue<string>() is { } finishReason)
                {
                    foreach (var entry in toolCallBuilders.OrderBy(item => item.Key))
                    {
                        yield return new ModelToolCallEvent(entry.Value.ToToolCall());
                    }
                    yield return new ModelCompletedEvent(finishReason);
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

    private sealed class ToolCallBuilder
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public readonly StringBuilder Arguments = new();

        public ToolCall ToToolCall() => new(Id, Name, Arguments.ToString());
    }
}
