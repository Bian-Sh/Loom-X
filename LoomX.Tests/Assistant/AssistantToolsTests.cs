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
        var properties = tool.ParametersSchema["properties"]!.AsObject();
        Assert.Contains("title", properties);
        Assert.Contains("question", properties);
        Assert.Contains("reason", properties);
        Assert.Contains("impact_summary", properties);
        Assert.Contains("allow_cancel", properties);
        Assert.Contains("fields", properties);
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
            field => Assert.Equal("one", field.DefaultOptionId),
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
        var result = JsonNode.Parse((await execution.WaitAsync(TimeSpan.FromSeconds(2))).Content)!;
        Assert.Equal("one", result["values"]!["single"]!.GetValue<string>());
        Assert.Equal(["one", "two"], result["values"]!["multi"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(5m, result["values"]!["count"]!.GetValue<decimal>());
        Assert.Equal("完成", result["values"]!["note"]!.GetValue<string>());
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
        Assert.Equal("{\"cancelled\":true,\"values\":{}}", result.Content);
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
        broker.PendingRequested += (_, request) => completion.TrySetResult(request);
        return completion;
    }

    private static UserDecisionBroker CreateBroker() =>
        new(NullLogger<UserDecisionBroker>.Instance);
}
