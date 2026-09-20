using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.UserDecisions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class AssistantToolsTests
{
    [Fact]
    public void RegisterAll_注册只读AskUser工具及完整请求Schema()
    {
        using var broker = CreateBroker();
        var registry = new ToolRegistry();

        AssistantTools.RegisterAll(registry, broker);

        Assert.True(registry.TryGet("assistant.ask_user", out var tool));
        Assert.NotNull(tool);
        Assert.Equal(ToolRiskLevel.Read, tool!.RiskLevel);
        Assert.Equal(Timeout.InfiniteTimeSpan, tool.Timeout);
        Assert.NotEqual(TimeSpan.FromSeconds(30), tool.Timeout);
        var properties = tool.ParametersSchema["properties"]!.AsObject();
        Assert.Contains("title", properties);
        Assert.Contains("question", properties);
        Assert.Contains("reason", properties);
        Assert.Contains("impact_summary", properties);
        Assert.Contains("allow_cancel", properties);
        Assert.Contains("fields", properties);
    }

    [Fact]
    public void RegisterAll_AskUser选择字段Schema要求非空Options()
    {
        using var broker = CreateBroker();
        var tool = GetTool(broker);
        var fieldSchema = tool.ParametersSchema["properties"]!["fields"]!["items"]!.AsObject();
        var constraints = fieldSchema["allOf"]!.AsArray();

        Assert.Equal(2, constraints.Count);
        Assert.All(constraints, constraint =>
        {
            var then = constraint!["then"]!.AsObject();
            Assert.Contains("options", then["required"]!.AsArray().Select(item => item!.GetValue<string>()));
            Assert.Equal(1, then["properties"]!["options"]!["minItems"]!.GetValue<int>());
        });
    }

    [Fact]
    public void RegisterAll_AskUser选择字段公开自由输入Schema()
    {
        using var broker = CreateBroker();
        var tool = GetTool(broker);
        var properties = tool.ParametersSchema["properties"]!["fields"]!["items"]!["properties"]!.AsObject();

        Assert.Equal("boolean", properties["allow_custom_input"]!["type"]!.GetValue<string>());
        Assert.False(properties["allow_custom_input"]!["default"]!.GetValue<bool>());
        Assert.Equal(200, properties["custom_input_placeholder"]!["maxLength"]!.GetValue<int>());
    }

    [Fact]
    public void RegisterAll_AskUser明确选择题同页自由输入建模规则()
    {
        using var broker = CreateBroker();
        var tool = GetTool(broker);
        var fieldsSchema = tool.ParametersSchema["properties"]!["fields"]!.AsObject();
        var fieldProperties = fieldsSchema["items"]!["properties"]!.AsObject();
        var fieldsDescription = fieldsSchema["description"]?.GetValue<string>() ?? string.Empty;
        var customInputDescription = fieldProperties["allow_custom_input"]!["description"]?.GetValue<string>() ?? string.Empty;
        var maxLengthDescription = fieldProperties["max_length"]!["description"]?.GetValue<string>() ?? string.Empty;

        Assert.Contains("每个字段独立分页", fieldsDescription, StringComparison.Ordinal);
        Assert.Contains("选项下方", customInputDescription, StringComparison.Ordinal);
        Assert.Contains("同一页", customInputDescription, StringComparison.Ordinal);
        Assert.Contains("不要新增 text 字段", customInputDescription, StringComparison.Ordinal);
        Assert.Contains("allow_custom_input", tool.Description, StringComparison.Ordinal);
        Assert.Contains("不要创建独立 text 字段", tool.Description, StringComparison.Ordinal);
        Assert.Contains("自由输入", maxLengthDescription, StringComparison.Ordinal);
        Assert.Contains("输入框80字", maxLengthDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterAll_AskUser描述为无需Skill或Bridge的通用交互()
    {
        using var broker = CreateBroker();
        var registry = new ToolRegistry();

        AssistantTools.RegisterAll(registry, broker);

        Assert.True(registry.TryGet("assistant.ask_user", out var tool));
        Assert.NotNull(tool);
        Assert.Contains("通用", tool!.Description, StringComparison.Ordinal);
        Assert.Contains("无需加载 Skill", tool.Description, StringComparison.Ordinal);
        Assert.Contains("无需 Browser Bridge 或 Chrome", tool.Description, StringComparison.Ordinal);
        Assert.Contains("测试", tool.Description, StringComparison.Ordinal);
        Assert.Contains("偏好", tool.Description, StringComparison.Ordinal);
        Assert.Contains("澄清", tool.Description, StringComparison.Ordinal);
        Assert.Contains("确认", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskUser_等待提交后返回字段Id映射并传递当前取消令牌()
    {
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");
        using var cancellation = new CancellationTokenSource();

        var execution = tool.Handler(CreateValidArguments(), cancellation.Token);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(execution.IsCompleted);
        Assert.Equal("配置方式", request.Request.Title);
        Assert.Equal("请选择配置方式", request.Request.Question);
        Assert.Equal("需要确认业务偏好", request.Request.Description);
        Assert.Equal("影响后续配置步骤", request.Request.ImpactSummary);
        Assert.True(request.Request.AllowCancel);
        var field = Assert.Single(request.Request.Fields);
        Assert.Equal("mode", field.Id);
        Assert.Equal(UserDecisionFieldType.SingleSelect, field.Type);
        Assert.True(broker.Submit(request.RequestId, new Dictionary<string, object?>
        {
            ["mode"] = "safe",
        }));

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));
        var json = JsonNode.Parse(result.Content)!.AsObject();
        Assert.True(result.Success);
        Assert.False(json["cancelled"]!.GetValue<bool>());
        Assert.Equal("safe", json["values"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task AskUser_把四类字段及约束转换为强类型请求()
    {
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");
        var arguments = new JsonObject
        {
            ["title"] = "完整字段",
            ["question"] = "请确认",
            ["allow_cancel"] = false,
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "single",
                    ["label"] = "单选",
                    ["type"] = "single_select",
                    ["options"] = new JsonArray(new JsonObject { ["id"] = "one", ["label"] = "一" }),
                    ["default_option_id"] = "one",
                    ["allow_custom_input"] = true,
                    ["custom_input_placeholder"] = "描述你的模式",
                    ["max_length"] = 120,
                },
                new JsonObject
                {
                    ["id"] = "multi",
                    ["label"] = "多选",
                    ["type"] = "multi_select",
                    ["options"] = new JsonArray(
                        new JsonObject { ["id"] = "one", ["label"] = "一" },
                        new JsonObject { ["id"] = "two", ["label"] = "二" }),
                    ["default_option_ids"] = new JsonArray("one"),
                    ["min_selections"] = 1,
                    ["max_selections"] = 2,
                },
                new JsonObject
                {
                    ["id"] = "count",
                    ["label"] = "数量",
                    ["type"] = "number",
                    ["default_number"] = 4,
                    ["min_number"] = 1,
                    ["max_number"] = 10,
                    ["step"] = 1,
                },
                new JsonObject
                {
                    ["id"] = "note",
                    ["label"] = "说明",
                    ["type"] = "text",
                    ["default_text"] = "普通说明",
                    ["is_multiline"] = true,
                    ["max_length"] = 200,
                },
            },
        };

        var execution = tool.Handler(arguments, CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(request.Request.AllowCancel);
        Assert.Collection(
            request.Request.Fields,
            field =>
            {
                Assert.Equal("one", field.DefaultOptionId);
                Assert.True(field.AllowCustomInput);
                Assert.Equal("描述你的模式", field.CustomInputPlaceholder);
                Assert.Equal(120, field.MaxLength);
            },
            field =>
            {
                Assert.Equal(["one"], field.DefaultOptionIds);
                Assert.Equal(1, field.MinSelections);
                Assert.Equal(2, field.MaxSelections);
            },
            field =>
            {
                Assert.Equal(4m, field.DefaultNumber);
                Assert.Equal(1m, field.MinNumber);
                Assert.Equal(10m, field.MaxNumber);
                Assert.Equal(1m, field.Step);
            },
            field =>
            {
                Assert.Equal("普通说明", field.DefaultText);
                Assert.True(field.IsMultiline);
                Assert.Equal(200, field.MaxLength);
            });

        Assert.True(broker.Submit(request.RequestId, new Dictionary<string, object?>
        {
            ["single"] = "one",
            ["multi"] = new[] { "one", "two" },
            ["count"] = 5m,
            ["note"] = "完成",
        }));
        var toolResult = await execution.WaitAsync(TimeSpan.FromSeconds(2));
        var result = JsonNode.Parse(toolResult.Content)!;
        Assert.Equal("one", result["values"]!["single"]!.GetValue<string>());
        Assert.Equal(["one", "two"], result["values"]!["multi"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(5m, result["values"]!["count"]!.GetValue<decimal>());
        Assert.Equal("完成", result["values"]!["note"]!.GetValue<string>());
        Assert.Empty(result["custom_inputs"]!.AsObject());
    }

    [Fact]
    public async Task AskUser_选择题自由输入返回CustomInputs原文()
    {
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");
        var arguments = CreateValidArguments();
        arguments["fields"]![0]!["allow_custom_input"] = true;

        var execution = tool.Handler(arguments, CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(broker.Submit(
            request.RequestId,
            UserDecisionBrokerTestExtensions.ClaimantId,
            new Dictionary<string, object?> { ["mode"] = null },
            new Dictionary<string, string> { ["mode"] = "我想逐步确认" }));

        var result = JsonNode.Parse((await execution).Content)!;
        Assert.Null(result["values"]!["mode"]);
        Assert.Equal("我想逐步确认", result["custom_inputs"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task AskUser_选择字段缺少选项返回安全可修复问题()
    {
        using var broker = CreateBroker();
        var tool = GetTool(broker);
        var arguments = CreateValidArguments();
        var field = arguments["fields"]![0]!.AsObject();
        field.Remove("options");
        field.Remove("default_option_id");

        var result = await tool.Handler(arguments, CancellationToken.None);
        var content = JsonNode.Parse(result.Content)!.AsObject();
        var issue = Assert.Single(content["issues"]!.AsArray());

        Assert.False(result.Success);
        Assert.Equal("invalid_request", content["error"]!.GetValue<string>());
        Assert.Equal("options_required", issue!["code"]!.GetValue<string>());
        Assert.Equal(0, issue["field_index"]!.GetValue<int>());
        Assert.DoesNotContain("模式", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("配置方式", result.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[server]\nhost = \"localhost\"\nport = 8080")]
    [InlineData("{\"model\":\"demo\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}")]
    [InlineData("{\"status\":\"ok\",\"items\":[1,2]}")]
    [InlineData("X-Tenant: acme")]
    [InlineData("Server: nginx")]
    [InlineData("Date: Wed, 16 Sep 2026 12:00:00 GMT")]
    [InlineData("Location: /next")]
    [InlineData("Tenant: acme")]
    public async Task AskUser_禁止完整正文配置块与Header进入Pending(string prohibitedContent)
    {
        using var broker = CreateBroker();
        PendingUserDecision? observed = null;
        broker.PendingRequested += (_, request) => observed = request;
        var tool = GetTool(broker);
        var arguments = CreateValidArguments();
        arguments["question"] = prohibitedContent;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await tool.Handler(arguments, cancellation.Token);

        Assert.False(result.Success);
        Assert.Null(observed);
        Assert.Equal("invalid_request", JsonNode.Parse(result.Content)!["error"]!.GetValue<string>());
        Assert.DoesNotContain(prohibitedContent, result.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("top")]
    [InlineData("field")]
    [InlineData("option")]
    public async Task AskUser_拒绝各层未知属性且不回显属性值(string level)
    {
        const string unknownValue = "Authorization: Bearer unknown-property-secret";
        using var broker = CreateBroker();
        PendingUserDecision? observed = null;
        broker.PendingRequested += (_, request) => observed = request;
        var tool = GetTool(broker);
        var arguments = CreateValidArguments();
        var field = arguments["fields"]![0]!.AsObject();
        var option = field["options"]![0]!.AsObject();
        var target = level switch
        {
            "top" => arguments,
            "field" => field,
            "option" => option,
            _ => throw new InvalidOperationException(),
        };
        target["unexpected"] = unknownValue;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await tool.Handler(arguments, cancellation.Token);

        Assert.False(result.Success);
        Assert.Null(observed);
        Assert.Equal("invalid_request", JsonNode.Parse(result.Content)!["error"]!.GetValue<string>());
        Assert.DoesNotContain(unknownValue, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskUser_普通短句单个选项与JsonSchema字段可以进入Pending()
    {
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        var arguments = CreateValidArguments();
        arguments["question"] = "请确认 JSON Schema 的 type 字段。";
        arguments["fields"]![0]!["options"] = new JsonArray(
            new JsonObject { ["id"] = "string", ["label"] = "string" });
        arguments["fields"]![0]!["default_option_id"] = "string";

        var execution = tool.Handler(arguments, CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(broker.Cancel(request.RequestId, "测试结束"));
        Assert.True((await execution.WaitAsync(TimeSpan.FromSeconds(2))).Success);
    }

    [Fact]
    public async Task AskUser_带空格普通短句可用于问题原因与合法选项()
    {
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        var arguments = CreateValidArguments();
        arguments["question"] = "Please choose: option A";
        arguments["reason"] = "Please explain why: normal business reason";
        arguments["fields"]![0]!["options"]![0]!["label"] = "Option A: recommended";

        var execution = tool.Handler(arguments, CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(broker.Cancel(request.RequestId, "测试结束"));
        Assert.True((await execution.WaitAsync(TimeSpan.FromSeconds(2))).Success);
    }

    [Fact]
    public async Task AskUser_敏感自由文本提交被拒绝且不会进入ToolResult()
    {
        const string sensitiveText = "Authorization: Bearer sk-proj-abcdefghijklmnop";
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");
        var arguments = new JsonObject
        {
            ["title"] = "补充说明",
            ["question"] = "请输入普通说明",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "note",
                    ["label"] = "说明",
                    ["type"] = "text",
                },
            },
        };

        var execution = tool.Handler(arguments, CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(broker.Submit(request.RequestId, new Dictionary<string, object?>
        {
            ["note"] = sensitiveText,
        }));
        Assert.False(execution.IsCompleted);
        Assert.True(broker.Cancel(request.RequestId, "用户取消"));

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(sensitiveText, result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-proj-", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskUser_取消后返回结构化空值而不暴露取消原因()
    {
        const string cancellationReason = "Authorization=Bearer cancellation-secret";
        using var broker = CreateBroker();
        var pending = CaptureNext(broker);
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");

        var execution = tool.Handler(CreateValidArguments(), CancellationToken.None);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(broker.Cancel(request.RequestId, cancellationReason));

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.Success);
        Assert.Equal("{\"cancelled\":true,\"values\":{},\"custom_inputs\":{}}", result.Content);
        Assert.DoesNotContain(cancellationReason, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskUser_敏感请求在进入Pending前失败且结果不回显原文()
    {
        const string sensitiveText = "Authorization: Bearer sk-proj-abcdefghijklmnop";
        using var broker = CreateBroker();
        PendingUserDecision? observed = null;
        broker.PendingRequested += (_, request) => observed = request;
        var tool = GetTool(broker);
        using var ownerScope = AssistantTools.BeginRun("assistant-run-test");
        var arguments = CreateValidArguments();
        arguments["question"] = sensitiveText;

        var result = await tool.Handler(arguments, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(observed);
        Assert.Equal("invalid_request", JsonNode.Parse(result.Content)!["error"]!.GetValue<string>());
        Assert.DoesNotContain(sensitiveText, result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-proj-", result.Content, StringComparison.Ordinal);
    }



    [Fact]
    public async Task AskUser_Handler拒绝前Session事件和下一轮请求只保留字段结构投影()
    {
        const string sensitiveText = "Authorization: Bearer rejected-before-handler-secret";
        using var broker = CreateBroker();
        var registry = new ToolRegistry();
        AssistantTools.RegisterAll(registry, broker);
        var arguments = CreateValidArguments();
        arguments["question"] = sensitiveText;
        arguments["fields"]![0]!["default_text"] = "[provider.headers]\nX-Custom = \"private\"";
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("ask-safe", "assistant.ask_user", arguments.ToJsonString())), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("请求无效。"), new ModelCompletedEvent("stop")]);
        var loop = new AgentLoop(model, registry, NullLogger<AgentLoop>.Instance);
        var session = new AgentSession();

        var events = new List<AgentEvent>();
        await foreach (var item in loop.RunAsync(session, "请询问用户")) events.Add(item);

        var safePayloads = new[]
        {
            session.Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls[0].ArgumentsJson,
            events.Single(item => item.Kind == AgentEventKind.MessageCompleted && item.Message?.ToolCalls.Count > 0)
                .Message!.ToolCalls[0].ArgumentsJson,
            model.Requests[1].Messages.Single(message => message.ToolCalls.Count > 0).ToolCalls[0].ArgumentsJson,
        };
        Assert.All(safePayloads, payload =>
        {
            Assert.DoesNotContain(sensitiveText, payload, StringComparison.Ordinal);
            Assert.DoesNotContain("private", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("配置方式", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("模式", payload, StringComparison.Ordinal);
        });
        Assert.Contains("field_count", safePayloads[0], StringComparison.Ordinal);
        Assert.Contains("single_select", safePayloads[0], StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidOptionalPropertyTypes()
    {
        yield return ["request", "reason", "123"];
        yield return ["request", "reason", "null"];
        yield return ["request", "impact_summary", "false"];
        yield return ["request", "allow_cancel", "\"yes\""];
        yield return ["field", "is_required", "1"];
        yield return ["field", "options", "{}"];
        yield return ["field", "options", "null"];
        yield return ["field", "default_option_id", "true"];
        yield return ["field", "default_option_ids", "\"safe\""];
        yield return ["field", "min_selections", "false"];
        yield return ["field", "max_selections", "\"2\""];
        yield return ["field", "default_number", "true"];
        yield return ["field", "min_number", "\"0\""];
        yield return ["field", "max_number", "[]"];
        yield return ["field", "step", "{}"];
        yield return ["field", "default_text", "42"];
        yield return ["field", "is_multiline", "\"false\""];
        yield return ["field", "max_length", "true"];
        yield return ["field", "allow_custom_input", "1"];
        yield return ["field", "custom_input_placeholder", "42"];
        yield return ["option", "description", "42"];
    }

    [Theory]
    [MemberData(nameof(InvalidOptionalPropertyTypes))]
    public async Task AskUser_已知Optional属性存在时错误Json类型统一返回InvalidRequest(
        string scope,
        string propertyName,
        string invalidJson)
    {
        using var broker = CreateBroker();
        broker.PendingRequested += (_, pending) =>
        {
            if (broker.TryClaim(pending.RequestId, "test-ui"))
            {
                broker.Cancel(pending.RequestId, "test-ui", "invalid-test-cleanup");
            }
        };
        var tool = GetTool(broker);
        var arguments = CreateArgumentsForOptionalProperty(scope, propertyName);
        var invalidValue = JsonNode.Parse(invalidJson);
        var field = arguments["fields"]![0]!.AsObject();
        var target = scope switch
        {
            "request" => arguments,
            "field" => field,
            "option" => field["options"]![0]!.AsObject(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        target[propertyName] = invalidValue;

        var result = await tool.Handler(arguments, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_request", JsonNode.Parse(result.Content)!["error"]!.GetValue<string>());
    }

    private static JsonObject CreateArgumentsForOptionalProperty(string scope, string propertyName)
    {
        var arguments = CreateValidArguments();
        var field = arguments["fields"]![0]!.AsObject();
        if (propertyName == "options")
        {
            field["type"] = "number";
            field.Remove("options");
            field.Remove("default_option_id");
        }
        else if (propertyName == "default_option_ids")
        {
            field["type"] = "text";
            field.Remove("options");
            field.Remove("default_option_id");
        }

        return arguments;
    }

    private static JsonObject CreateValidArguments() => new()
    {
        ["title"] = "配置方式",
        ["question"] = "请选择配置方式",
        ["reason"] = "需要确认业务偏好",
        ["impact_summary"] = "影响后续配置步骤",
        ["allow_cancel"] = true,
        ["fields"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "mode",
                ["label"] = "模式",
                ["type"] = "single_select",
                ["is_required"] = true,
                ["options"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "safe",
                        ["label"] = "安全模式",
                        ["description"] = "保留现有配置",
                    },
                    new JsonObject
                    {
                        ["id"] = "fast",
                        ["label"] = "快速模式",
                    },
                },
                ["default_option_id"] = "safe",
            },
        },
    };

    private static ToolDefinition GetTool(UserDecisionBroker broker)
    {
        var registry = new ToolRegistry();
        AssistantTools.RegisterAll(registry, broker);
        Assert.True(registry.TryGet("assistant.ask_user", out var tool));
        return tool!;
    }

    private static TaskCompletionSource<PendingUserDecision> CaptureNext(UserDecisionBroker broker)
    {
        var completion = new TaskCompletionSource<PendingUserDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        broker.PendingRequested += (_, request) =>
        {
            broker.TryClaim(request.RequestId, UserDecisionBrokerTestExtensions.ClaimantId);
            completion.TrySetResult(request);
        };
        return completion;
    }

    private static UserDecisionBroker CreateBroker() =>
        new(NullLogger<UserDecisionBroker>.Instance);
}
