using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>工具审批门：返回 true 批准执行，false 拒绝。仅在逐条批准模式下对写/删工具触发。</summary>
public delegate Task<bool> ToolApprovalGate(ToolCall toolCall, ToolDefinition tool, CancellationToken cancellationToken);

/// <summary>
/// 小助手最小 Agent 循环：
/// 用户 → 模型 →（工具调用 → 工具结果 → 模型）→ 回答。
/// 支持 streaming、tool calling、cancellation、timeout、max steps 与错误恢复。
/// </summary>
public sealed class AgentLoop
{
    private readonly IModelClient modelClient;
    private readonly ToolRegistry toolRegistry;
    private readonly ILogger<AgentLoop> logger;
    private readonly ToolApprovalGate? approvalGate;

    public AgentLoop(
        IModelClient modelClient,
        ToolRegistry toolRegistry,
        ILogger<AgentLoop> logger,
        ToolApprovalGate? approvalGate = null)
    {
        this.modelClient = modelClient;
        this.toolRegistry = toolRegistry;
        this.logger = logger;
        this.approvalGate = approvalGate;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        AgentSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State == AgentSessionState.Running)
        {
            throw new InvalidOperationException("会话正在运行中。");
        }

        session.MarkRunning();
        session.AddMessage(ChatMessage.User(userMessage));
        yield return AgentEvent.Create(session.Id, AgentEventKind.SessionStarted);

        var completed = false;
        var cancelled = false;
        string? failedDetail = null;

        for (var step = 0; step < session.Options.MaxSteps && !completed && !cancelled && failedDetail is null; step++)
        {
            using var stepTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stepTimeout.CancelAfter(session.Options.ModelTimeout);

            var textBuilder = new StringBuilder();
            var toolCalls = new List<ToolCall>();
            var finishReason = "stop";

            // 逐条拉取模型流。yield 不允许出现在带 catch 的 try 内，因此仅在 try 中移动枚举器，事件在 try 外处理。
            await using var enumerator = modelClient
                .StreamAsync(new ModelRequest(session.Messages, toolRegistry.All), stepTimeout.Token)
                .GetAsyncEnumerator(stepTimeout.Token);

            while (failedDetail is null && !cancelled)
            {
                ModelStreamEvent? streamEvent = null;
                var moved = false;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                    if (moved) streamEvent = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                    }
                    else
                    {
                        logger.LogWarning("小助手模型请求超时 {SessionId} 步骤 {Step}", session.Id, step + 1);
                        failedDetail = "模型请求超时。";
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "小助手模型请求失败 {SessionId} 步骤 {Step}", session.Id, step + 1);
                    failedDetail = "模型请求失败。";
                }

                if (!moved || streamEvent is null) break;

                switch (streamEvent)
                {
                    case TextDeltaEvent textDelta:
                        textBuilder.Append(textDelta.Text);
                        yield return AgentEvent.Create(session.Id, AgentEventKind.TextDelta) with { Text = textDelta.Text };
                        break;
                    case ModelToolCallEvent toolCallEvent:
                        toolCalls.Add(toolCallEvent.ToolCall);
                        break;
                    case ModelCompletedEvent completedEvent:
                        finishReason = completedEvent.FinishReason;
                        break;
                }
            }

            if (cancelled || failedDetail is not null) break;

            session.AddMessage(ChatMessage.AssistantToolCalls(
                toolCalls,
                textBuilder.Length > 0 ? textBuilder.ToString() : null));

            if (toolCalls.Count == 0)
            {
                completed = true;
                logger.LogDebug("小助手任务完成 {SessionId} 步骤 {Step} 结束原因 {FinishReason}", session.Id, step + 1, finishReason);
                break;
            }

            foreach (var toolCall in toolCalls)
            {
                yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallStarted) with
                {
                    ToolName = toolCall.Name,
                    ToolCallId = toolCall.Id,
                };

                // 逐条批准模式：写/删工具先等用户批准
                if (approvalGate is not null
                    && toolRegistry.TryGet(toolCall.Name, out var pendingTool)
                    && pendingTool is not null
                    && RequiresApproval(pendingTool.RiskLevel))
                {
                    yield return AgentEvent.Create(session.Id, AgentEventKind.ToolApprovalRequested) with
                    {
                        ToolName = toolCall.Name,
                        ToolCallId = toolCall.Id,
                        Detail = SummarizeArguments(toolCall.ArgumentsJson),
                    };

                    bool approved;
                    try
                    {
                        approved = await approvalGate(toolCall, pendingTool, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }

                    if (!approved)
                    {
                        logger.LogInformation("小助手工具调用被用户拒绝 {ToolName}", toolCall.Name);
                        session.AddMessage(ChatMessage.ToolResult(toolCall, "用户拒绝了这次修改操作，没有执行。"));
                        yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallCompleted) with
                        {
                            ToolName = toolCall.Name,
                            ToolCallId = toolCall.Id,
                            Success = false,
                            Detail = "已被你拒绝，未执行。",
                        };
                        continue;
                    }
                }

                var (result, toolCancelled) = await ExecuteToolAsync(toolCall, cancellationToken);
                if (toolCancelled)
                {
                    cancelled = true;
                    break;
                }

                session.AddMessage(ChatMessage.ToolResult(toolCall, result!.Content));

                yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallCompleted) with
                {
                    ToolName = toolCall.Name,
                    ToolCallId = toolCall.Id,
                    Success = result.Success,
                    Detail = result.Success ? null : result.Content,
                };
            }
        }

        if (cancelled)
        {
            session.MarkCancelled();
            yield break;
        }

        if (completed)
        {
            session.MarkCompleted();
            yield return AgentEvent.Create(session.Id, AgentEventKind.TaskCompleted);
            yield break;
        }

        session.MarkFailed();
        yield return AgentEvent.Create(session.Id, AgentEventKind.TaskFailed) with
        {
            Detail = failedDetail ?? $"超过最大步骤数 {session.Options.MaxSteps}。",
        };
    }

    /// <summary>修改类操作（写/删）才需要逐条批准；只读与外部测试直接放行。</summary>
    private static bool RequiresApproval(ToolRiskLevel riskLevel) =>
        riskLevel is ToolRiskLevel.Write or ToolRiskLevel.Destructive;

    /// <summary>审批卡片展示的参数摘要：截断过长的参数 JSON，仅用于 UI 展示。</summary>
    private static string SummarizeArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return string.Empty;
        var compact = argumentsJson.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }

    /// <summary>
    /// 执行工具。Cancelled = true 表示用户取消了整个会话（区别于工具自身超时）。
    /// </summary>
    private async Task<(ToolResult? Result, bool Cancelled)> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken)
    {
        if (!toolRegistry.TryGet(toolCall.Name, out var tool) || tool is null)
        {
            logger.LogWarning("小助手调用了未注册的工具 {ToolName}", toolCall.Name);
            return (ToolResult.Fail($"未注册的工具：{toolCall.Name}"), false);
        }

        JsonNode? arguments;
        try
        {
            arguments = string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? null : JsonNode.Parse(toolCall.ArgumentsJson);
        }
        catch (JsonException)
        {
            return (ToolResult.Fail("工具参数不是有效的 JSON。"), false);
        }

        using var toolTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        toolTimeout.CancelAfter(tool.Timeout);
        try
        {
            return (await tool.Handler(arguments, toolTimeout.Token), false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) return (null, true);
            logger.LogWarning("小助手工具执行超时 {ToolName}", tool.Name);
            return (ToolResult.Fail("工具执行超时。"), false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "小助手工具执行失败 {ToolName}", tool.Name);
            return (ToolResult.Fail($"工具执行失败：{exception.Message}"), false);
        }
    }
}
