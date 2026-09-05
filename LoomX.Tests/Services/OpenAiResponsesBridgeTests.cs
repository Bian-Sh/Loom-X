using System.Text.Json.Nodes;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class OpenAiResponsesBridgeTests
{
    [Fact]
    public void CreateResponsesRequest_ConvertsChatCompletionFieldsAndFunctionTools()
    {
        var chatRequest = JsonNode.Parse("""
        {
          "model": "configured-model",
          "messages": [{"role": "user", "content": "sensitive prompt"}],
          "max_tokens": 64,
          "tools": [{"type": "function", "function": {"name": "read_file", "description": "读取文件", "parameters": {"type": "object"}}}],
          "tool_choice": {"type": "function", "function": {"name": "read_file"}}
        }
        """)!.AsObject();

        var result = OpenAiResponsesBridge.CreateResponsesRequest(chatRequest);

        Assert.Equal("configured-model", result["model"]!.GetValue<string>());
        Assert.Equal("sensitive prompt", result["input"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(64, result["max_output_tokens"]!.GetValue<int>());
        Assert.Null(result["messages"]);
        Assert.Null(result["max_tokens"]);
        Assert.Equal("read_file", result["tools"]![0]!["name"]!.GetValue<string>());
        Assert.Null(result["tools"]![0]!["function"]);
        Assert.Equal("read_file", result["tool_choice"]!["name"]!.GetValue<string>());
        Assert.Null(result["tool_choice"]!["function"]);
    }

    [Fact]
    public void CreateChatCompletionsSse_ConvertsTextToolCallsAndCompletion()
    {
        const string responsesSse = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_123","created_at":123,"model":"configured-model"}}

        event: response.output_item.added
        data: {"type":"response.output_item.added","item":{"id":"msg_1","type":"message","role":"assistant"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","delta":"healthy"}

        event: response.output_item.added
        data: {"type":"response.output_item.added","item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"read_file"}}

        event: response.function_call_arguments.delta
        data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":"{\"path\":\"a.cs\"}"}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_123"}}

        data: [DONE]

        """;

        var result = OpenAiResponsesBridge.CreateChatCompletionsSse(responsesSse, "configured-model");

        Assert.Contains("\"role\":\"assistant\"", result);
        Assert.Contains("\"content\":\"healthy\"", result);
        Assert.Contains("\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\"}}]", result);
        var chunks = result.Split('\n')
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal))
            .Select(line => JsonNode.Parse(line[6..])!.AsObject())
            .ToArray();
        var argumentDelta = chunks
            .SelectMany(chunk => chunk["choices"]!.AsArray())
            .Select(choice => choice!["delta"]?["tool_calls"]?[0]?["function"]?["arguments"]?.GetValue<string>())
            .Single(value => value is not null);
        Assert.Equal("{\"path\":\"a.cs\"}", argumentDelta);
        Assert.Contains("\"finish_reason\":\"tool_calls\"", result);
        Assert.EndsWith("data: [DONE]\n\n", result);
    }

    [Fact]
    public void CreateChatCompletionsResponse_ConvertsMessageAndFunctionCall()
    {
        var responsesResponse = JsonNode.Parse("""
        {
          "id": "resp_123",
          "created_at": 123,
          "output": [
            {
              "type": "message",
              "role": "assistant",
              "content": [{"type": "output_text", "text": "healthy"}]
            },
            {
              "type": "function_call",
              "call_id": "call_1",
              "name": "read_file",
              "arguments": "{\"path\":\"a.cs\"}"
            }
          ]
        }
        """)!.AsObject();

        var result = OpenAiResponsesBridge.CreateChatCompletionsResponse(responsesResponse, "configured-model");

        Assert.Equal("resp_123", result["id"]!.GetValue<string>());
        Assert.Equal("configured-model", result["model"]!.GetValue<string>());
        Assert.Equal("healthy", result["choices"]![0]!["message"]!["content"]!.GetValue<string>());
        Assert.Equal("call_1", result["choices"]![0]!["message"]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("read_file", result["choices"]![0]!["message"]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("tool_calls", result["choices"]![0]!["finish_reason"]!.GetValue<string>());
    }

    [Fact]
    public void CreateResponsesRequest_ConvertsToolHistoryAndMultimodalContent()
    {
        var chatRequest = JsonNode.Parse("""
        {
          "messages": [
            {"role":"user","content":[{"type":"text","text":"读取图片"},{"type":"image_url","image_url":{"url":"https://example.invalid/image.png","detail":"low"}}]},
            {"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.cs\"}"}}]},
            {"role":"tool","tool_call_id":"call_1","content":"工具结果"}
          ]
        }
        """)!.AsObject();

        var result = OpenAiResponsesBridge.CreateResponsesRequest(chatRequest);
        var input = result["input"]!.AsArray();

        Assert.Equal("input_text", input[0]!["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("input_image", input[0]!["content"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("low", input[0]!["content"]![1]!["detail"]!.GetValue<string>());
        Assert.Equal("function_call", input[1]!["type"]!.GetValue<string>());
        Assert.Equal("call_1", input[1]!["call_id"]!.GetValue<string>());
        Assert.Equal("function_call_output", input[2]!["type"]!.GetValue<string>());
        Assert.Equal("工具结果", input[2]!["output"]!.GetValue<string>());
    }
}
