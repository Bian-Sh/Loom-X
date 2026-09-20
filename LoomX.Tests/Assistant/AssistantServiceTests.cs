using Xunit;
using LoomX.Assistant;
using LoomX.Assistant.Browser;
using LoomX.Assistant.UserDecisions;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoomX.Tests.Assistant;

/// <summary>
/// AssistantService 门面：事件流驱动、模型未配置错误、会话持久化与恢复。
/// </summary>
public sealed class AssistantServiceTests : IDisposable
{
    private readonly string rootDirectory = Path.Combine(Path.GetTempPath(), $"loomx-svc-sessions-{Guid.NewGuid():N}");
    private readonly TestDbContextFactory dbContextFactory;

    public AssistantServiceTests()
    {
        Directory.CreateDirectory(rootDirectory);
        var databasePath = Path.Combine(rootDirectory, "LoomX.db");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        dbContextFactory = new TestDbContextFactory(options);
        using var context = new ConfigurationDbContext(options);
        ConfigurationDatabase.InitializeAsync(context).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void NewSession_SystemPrompt_DeclaresResearchChannelsAndChallengeHandoff()
    {
        var service = CreateService(new StubModelClientFactory(null));

        var prompt = Assert.IsType<string>(service.CurrentSession.Options.SystemPrompt);
        var nativeIndex = prompt.IndexOf("优先使用模型原生或已有的官方资料能力", StringComparison.Ordinal);
        var browserIndex = prompt.IndexOf("其次用 Browser Bridge", StringComparison.Ordinal);
        var askUserIndex = prompt.IndexOf("无可用通道时用 assistant.ask_user", StringComparison.Ordinal);

        Assert.True(nativeIndex >= 0 && nativeIndex < browserIndex && browserIndex < askUserIndex);
        Assert.Contains("browser.open", prompt);
        Assert.Contains("browser.read", prompt);
        Assert.Contains("browser.wait", prompt);
        Assert.Contains("登录", prompt);
        Assert.Contains("CAPTCHA", prompt);
        Assert.Contains("Cloudflare", prompt);
        Assert.Contains("JS challenge", prompt);
        Assert.Contains("立即暂停并交还用户", prompt);
        Assert.Contains("禁止绕过网站安全机制", prompt);
        Assert.Contains("当前 Assistant Session ID", prompt);
        Assert.Contains(service.CurrentSession.Id, prompt, StringComparison.Ordinal);
        Assert.Contains("browser.bridge_start", prompt, StringComparison.Ordinal);
        Assert.Contains("browser.bridge_stop", prompt, StringComparison.Ordinal);
        AssertNoSearchSecretConfiguration(prompt);
    }


    [Fact]
    public void NewSession_SystemPrompt_允许直接测试AskUser且不依赖外部能力()
    {
        var service = CreateService(new StubModelClientFactory(null));

        var prompt = Assert.IsType<string>(service.CurrentSession.Options.SystemPrompt);

        Assert.Contains("用户明确要求测试 AskUser 时直接调用", prompt, StringComparison.Ordinal);
        Assert.Contains("不需要加载 Skill", prompt, StringComparison.Ordinal);
        Assert.Contains("不需要 Browser Bridge 或 Chrome", prompt, StringComparison.Ordinal);
        Assert.Contains("偏好收集", prompt, StringComparison.Ordinal);
        Assert.Contains("歧义澄清", prompt, StringComparison.Ordinal);
        Assert.Contains("行动确认", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 删除Session会被动清理意外残留的Bridge租约()
    {
        var lifecycle = new FakeBrowserBridgeLifecycle();
        var leases = new BrowserBridgeLeaseManager(lifecycle, NullLogger<BrowserBridgeLeaseManager>.Instance);
        var service = CreateService(new StubModelClientFactory(null), browserBridgeLeaseManager: leases);
        var sessionId = service.CurrentSession.Id;
        await leases.AcquireAsync(sessionId);

        await service.DeleteSessionAsync(sessionId);

        Assert.Empty(leases.ActiveSessionIds);
        Assert.False(lifecycle.IsListening);
        Assert.Equal(1, lifecycle.StopCount);
    }
    [Fact]
    public async Task SendAsync_StreamsEvents_AndPersistsSession()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("你好，"), new TextDeltaEvent("我是小助手。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        var events = new List<AgentEvent>();

        await foreach (var agentEvent in service.SendAsync("你好"))
        {
            events.Add(agentEvent);
        }

        Assert.Contains(events, item => item.Kind == AgentEventKind.SessionStarted);
        Assert.Equal(2, events.Count(item => item.Kind == AgentEventKind.TextDelta));
        Assert.Contains(events, item => item.Kind == AgentEventKind.TaskCompleted);
        Assert.Equal(AgentSessionState.Completed, service.CurrentSession.State);

        // 运行结束后会话已持久化
        var summaries = service.ListSessions();
        var summary = Assert.Single(summaries);
        Assert.Equal("你好", summary.Title);
    }

    [Fact]
    public async Task SendAsync_ModelNotConfigured_Throws()
    {
        var service = CreateService(new StubModelClientFactory(null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var unused in service.SendAsync("你好")) { }
        });

        Assert.Contains("模型未配置", exception.Message);
    }

    [Fact]
    public async Task SendAsync_EmptyMessage_Throws()
    {
        var service = CreateService(new StubModelClientFactory(new ScriptedModelClient(
            [new ModelCompletedEvent("stop")])));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var unused in service.SendAsync("   ")) { }
        });
    }

    [Fact]
    public async Task SendAsync_ConcurrentRequest_IsRejectedBeforeModelCreationCompletes()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("第一条完成"), new ModelCompletedEvent("stop")]);
        var factory = new BlockingModelClientFactory(model);
        var service = CreateService(factory);

        var firstTask = Task.Run(async () =>
        {
            await foreach (var unused in service.SendAsync("第一条")) { }
        });
        await factory.FirstCreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var unused in service.SendAsync("第二条")) { }
        }).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(exception);
        factory.ReleaseFirstCreate.TrySetResult(true);
        await firstTask;
        Assert.DoesNotContain(service.CurrentSession.Messages, message => message.Content == "第二条");
    }

    [Fact]
    public async Task LoadSession_RestoresHistory_AsCurrentSession()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("已查到。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        await foreach (var unused in service.SendAsync("查询 Provider")) { }

        var sessionId = service.CurrentSession.Id;
        service.NewSession();
        Assert.NotEqual(sessionId, service.CurrentSession.Id);

        Assert.True(await service.LoadSessionAsync(sessionId));
        Assert.Equal(sessionId, service.CurrentSession.Id);
        Assert.Contains(service.CurrentSession.Messages, message => message.Content == "查询 Provider");
    }

    [Fact]
    public async Task LoadSession_继续发送时使用当前系统策略且不重复旧策略()
    {
        var sessionDirectory = Path.Combine(rootDirectory, $"sessions-{Guid.NewGuid():N}");
        var sessionStore = new AssistantSessionStore(sessionDirectory);
        const string oldPrompt = "旧策略：只使用 Browser Bridge，并允许自动绕过 JS challenge。";
        var oldSession = new AgentSession(new AgentSessionOptions
        {
            MaxSteps = 16,
            SystemPrompt = oldPrompt,
        });
        oldSession.RestoreMessage(ChatMessage.User("历史问题"));
        oldSession.RestoreMessage(ChatMessage.Assistant("历史回答"));
        await sessionStore.SaveAsync(oldSession);

        var model = new ScriptedModelClient(
            [new TextDeltaEvent("继续回答"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), sessionStore: sessionStore);

        Assert.True(await service.LoadSessionAsync(oldSession.Id));
        await foreach (var unused in service.SendAsync("继续发送")) { }

        var request = Assert.Single(model.Requests);
        var systemMessage = Assert.Single(request.Messages, message => message.Role == ChatRole.System);
        var prompt = Assert.IsType<string>(systemMessage.Content);
        var nativeIndex = prompt.IndexOf("优先使用模型原生或已有的官方资料能力", StringComparison.Ordinal);
        var browserIndex = prompt.IndexOf("其次用 Browser Bridge", StringComparison.Ordinal);
        var askUserIndex = prompt.IndexOf("无可用通道时用 assistant.ask_user", StringComparison.Ordinal);

        Assert.True(nativeIndex >= 0 && nativeIndex < browserIndex && browserIndex < askUserIndex);
        Assert.Contains("Cloudflare", prompt);
        Assert.Contains("JS challenge", prompt);
        Assert.Contains("立即暂停并交还用户", prompt);
        Assert.Contains("禁止绕过网站安全机制", prompt);
        Assert.Contains("当前 Assistant Session ID", prompt);
        Assert.Contains(service.CurrentSession.Id, prompt, StringComparison.Ordinal);
        Assert.Contains("browser.bridge_start", prompt, StringComparison.Ordinal);
        Assert.Contains("browser.bridge_stop", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(oldPrompt, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Messages, message =>
            message.Role == ChatRole.System && message.Content == oldPrompt);
    }

    [Fact]
    public async Task SendAsync_SwitchSessionDuringStreaming_PersistsOriginalRunWithoutPollutingViewedSession()
    {
        var model = new SessionSwitchModelClient();
        var service = CreateService(new StubModelClientFactory(model));
        await foreach (var unused in service.SendAsync("历史问题")) { }
        var viewedSessionId = service.CurrentSession.Id;

        var runningSessionId = service.NewSession().Id;
        var runTask = Task.Run(async () =>
        {
            await foreach (var unused in service.SendAsync("流式问题")) { }
        });

        await model.StreamingPaused.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await service.LoadSessionAsync(viewedSessionId));
        model.ContinueStreaming.TrySetResult(true);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(viewedSessionId, service.CurrentSession.Id);
        Assert.DoesNotContain(service.CurrentSession.Messages, message => message.Content?.Contains("流式") == true);

        Assert.True(await service.LoadSessionAsync(runningSessionId));
        Assert.Contains(service.CurrentSession.Messages,
            message => message.Role == ChatRole.Assistant && message.Content == "第一段第二段");
    }

    [Fact]
    public async Task NewSession_ClearsCurrentHistory()
    {
        var model = new ScriptedModelClient(
            [new TextDeltaEvent("回答"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model));
        await foreach (var unused in service.SendAsync("问题")) { }

        service.NewSession();

        Assert.DoesNotContain(service.CurrentSession.Messages, message => message.Content == "问题");
    }

    [Fact]
    public async Task AskEachTime_RejectedWriteTool_NotExecuted()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", """{"value":1}""")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("好的。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AskEachTime);
        service.ApprovalHandler = _ => Task.FromResult(false);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.False(executed);
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolApprovalRequested && item.ToolName == "mock.write_thing");
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolCallCompleted && item.Success == false);
        Assert.Contains(service.CurrentSession.Messages, message => message.Role == ChatRole.Tool && message.Content!.Contains("拒绝"));
    }

    [Fact]
    public async Task AskEachTime_ApprovedWriteTool_Executed()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", """{"value":1}""")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("改好了。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AskEachTime);

        ToolApprovalRequest? captured = null;
        service.ApprovalHandler = request =>
        {
            captured = request;
            return Task.FromResult(true);
        };

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.True(executed);
        Assert.NotNull(captured);
        Assert.Equal("mock.write_thing", captured!.ToolName);
        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolCallCompleted && item.Success == true);
    }

    [Fact]
    public async Task AutoApprove_WriteToolRunsWithoutApproval()
    {
        var executed = false;
        var registry = CreateWriteToolRegistry(() => executed = true);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "mock.write_thing", "{}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("改好了。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, AssistantPermissionMode.AutoApprove);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in service.SendAsync("改一下")) events.Add(agentEvent);

        Assert.True(executed);
        Assert.DoesNotContain(events, item => item.Kind == AgentEventKind.ToolApprovalRequested);
    }

    [Fact]
    public async Task SendAsync_AskUser提交后保留实际文本并继续最终回答()
    {
        const string originalText = "自由文本原文-需要进入模型上下文-981273";
        using var broker = CreateDecisionBroker();
        var pending = CaptureNext(broker);
        var registry = CreateAskUserRegistry(broker);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("ask_1", "assistant.ask_user", AskUserArguments)), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已按安全模式继续。"), new ModelCompletedEvent("stop")]);
        var service = CreateService(new StubModelClientFactory(model), registry, userDecisionBroker: broker);
        var events = new List<AgentEvent>();

        var runTask = Task.Run(async () =>
        {
            await foreach (var agentEvent in service.SendAsync("请继续配置")) events.Add(agentEvent);
        });
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(service.CurrentSession.Messages,
            message => message.Role == ChatRole.Assistant && message.ToolCalls.Any(call => call.Name == "assistant.ask_user"));

        Assert.True(broker.Submit(request.RequestId, new Dictionary<string, object?>
        {
            ["mode"] = "safe",
            ["count"] = 3m,
            ["note"] = originalText,
        }));
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        var messages = service.CurrentSession.Messages;
        var toolMessage = Assert.Single(messages, message => message.Role == ChatRole.Tool);
        Assert.Equal("assistant.ask_user", toolMessage.ToolName);
        var toolResult = System.Text.Json.Nodes.JsonNode.Parse(toolMessage.Content!)!;
        Assert.Equal("safe", toolResult["values"]!["mode"]!.GetValue<string>());
        Assert.Equal(3m, toolResult["values"]!["count"]!.GetValue<decimal>());
        Assert.Equal(originalText, toolResult["values"]!["note"]!.GetValue<string>());
        Assert.Empty(toolResult["custom_inputs"]!.AsObject());
        Assert.Contains(messages, message => message.Role == ChatRole.Assistant && message.Content == "已按安全模式继续。");
        Assert.Contains(events, item => item.Kind == AgentEventKind.TaskCompleted);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(model.Requests[1].Messages,
            message => message.Role == ChatRole.Tool && message.ToolName == "assistant.ask_user");
        Assert.Contains(model.Requests[1].Messages,
            message => message.Role == ChatRole.Tool
                && message.Content?.Contains(originalText, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Cancel_取消等待中的AskUser并结束当前运行()
    {
        using var broker = CreateDecisionBroker();
        var pending = CaptureNext(broker);
        var service = CreateService(
            new StubModelClientFactory(CreateAskUserModel()),
            CreateAskUserRegistry(broker),
            userDecisionBroker: broker);
        var runningSession = service.CurrentSession;
        var runTask = RunToCompletionAsync(service, "等待选择");
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        service.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AgentSessionState.Cancelled, runningSession.State);
        var cancelledToolResult = Assert.Single(runningSession.Messages, message => message.Role == ChatRole.Tool);
        Assert.Equal("ask_1", cancelledToolResult.ToolCallId);
        Assert.Contains("\"cancelled\":true", cancelledToolResult.Content, StringComparison.Ordinal);
        Assert.False(broker.Submit(request.RequestId, new Dictionary<string, object?> { ["mode"] = "safe" }));
    }

    [Fact]
    public async Task NewSession_取消等待中的AskUser并切换到空会话()
    {
        using var broker = CreateDecisionBroker();
        var pending = CaptureNext(broker);
        var service = CreateService(
            new StubModelClientFactory(CreateAskUserModel()),
            CreateAskUserRegistry(broker),
            userDecisionBroker: broker);
        var runningSession = service.CurrentSession;
        var runTask = RunToCompletionAsync(service, "等待选择");
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var newSession = service.NewSession();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotEqual(runningSession.Id, newSession.Id);
        Assert.Same(newSession, service.CurrentSession);
        Assert.Equal(AgentSessionState.Cancelled, runningSession.State);
        Assert.False(broker.Submit(request.RequestId, new Dictionary<string, object?> { ["mode"] = "safe" }));
    }

    [Fact]
    public async Task SendAsync_外部取消令牌取消等待中的AskUser()
    {
        using var broker = CreateDecisionBroker();
        var pending = CaptureNext(broker);
        var service = CreateService(
            new StubModelClientFactory(CreateAskUserModel()),
            CreateAskUserRegistry(broker),
            userDecisionBroker: broker);
        using var cancellation = new CancellationTokenSource();
        var runningSession = service.CurrentSession;
        var runTask = RunToCompletionAsync(service, "等待选择", cancellation.Token);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AgentSessionState.Cancelled, runningSession.State);
        Assert.False(broker.Submit(request.RequestId, new Dictionary<string, object?> { ["mode"] = "safe" }));
    }

    [Fact]
    public async Task SendAsync_同一运行的Request与Finally取消使用相同Owner()
    {
        using var broker = new RecordingUserDecisionBroker();
        var pending = CaptureNext(broker);
        var service = CreateService(
            new StubModelClientFactory(CreateAskUserModel()),
            CreateAskUserRegistry(broker),
            userDecisionBroker: broker);
        using var cancellation = new CancellationTokenSource();
        var runTask = RunToCompletionAsync(service, "等待选择", cancellation.Token);
        var request = await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(runTask.IsCompleted);
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([request.OwnerId], broker.RequestOwnerIds);
        Assert.Equal(request.OwnerId, Assert.Single(broker.CancelOwnerIds));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   \n  ", null)]
    [InlineData("简单标题", "简单标题")]
    [InlineData("“带引号的标题”", "带引号的标题")]
    [InlineData("前置说明。\n第二行才是标题：这里的字也很多，超过二十个字就会被硬裁掉", "前置说明")]
    [InlineData("标题：", "标题")] // 尾部标点被裁掉后仍有效
    public void NormalizeTitle_TakesFirstMeaningfulLine_TrimsQuotes_CapsTo20(string? raw, string? expected)
    {
        Assert.Equal(expected, AssistantService.NormalizeTitle(raw));
    }

    private static ToolRegistry CreateWriteToolRegistry(Action onExecuted)
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolDefinition
        {
            Name = "mock.write_thing",
            Description = "模拟写操作",
            ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object","properties":{}}""")!,
            RiskLevel = ToolRiskLevel.Write,
            Handler = (_, _) =>
            {
                onExecuted();
                return Task.FromResult(ToolResult.Ok("""{"ok":true}"""));
            },
        });
        return registry;
    }

    private AssistantService CreateService(
        AssistantModelClientFactory factory,
        ToolRegistry? registry = null,
        AssistantPermissionMode? permissionMode = null,
        IUserDecisionBroker? userDecisionBroker = null,
        AssistantSessionStore? sessionStore = null,
        BrowserBridgeLeaseManager? browserBridgeLeaseManager = null)
    {
        AssistantPreferencesStore? preferencesStore = null;
        if (permissionMode is not null)
        {
            preferencesStore = new AssistantPreferencesStore(dbContextFactory);
            preferencesStore.Save(new AssistantPreferences { PermissionMode = permissionMode.Value });
        }

        return new AssistantService(
            factory,
            registry ?? new ToolRegistry(),
            sessionStore ?? new AssistantSessionStore(Path.Combine(rootDirectory, $"sessions-{Guid.NewGuid():N}")),
            NullLoggerFactory.Instance,
            preferencesStore,
            userDecisionBroker ?? CreateDecisionBroker(),
            browserBridgeLeaseManager);
    }

    private const string AskUserArguments = """
        {
          "title": "配置方式",
          "question": "请选择配置方式",
          "reason": "需要确认业务偏好",
          "impact_summary": "影响后续配置步骤",
          "allow_cancel": true,
          "fields": [
            {
              "id": "mode",
              "label": "模式",
              "type": "single_select",
              "is_required": true,
              "options": [
                { "id": "safe", "label": "安全模式" },
                { "id": "fast", "label": "快速模式" }
              ]
            },
            {
              "id": "count",
              "label": "数量",
              "type": "number"
            },
            {
              "id": "note",
              "label": "补充说明",
              "type": "text"
            }
          ]
        }
        """;

    private static ScriptedModelClient CreateAskUserModel() => new(
        [new ModelToolCallEvent(new ToolCall("ask_1", "assistant.ask_user", AskUserArguments)), new ModelCompletedEvent("tool_calls")]);

    private static ToolRegistry CreateAskUserRegistry(IUserDecisionBroker broker)
    {
        var registry = new ToolRegistry();
        AssistantTools.RegisterAll(registry, broker);
        return registry;
    }

    private static UserDecisionBroker CreateDecisionBroker() =>
        new(NullLogger<UserDecisionBroker>.Instance);

    private static void AssertNoSearchSecretConfiguration(string content)
    {
        foreach (var forbidden in new[]
        {
            "SEARCH_API_KEY",
            "SERPAPI_API_KEY",
            "TAVILY_API_KEY",
            "BRAVE_SEARCH_API_KEY",
            "BING_SEARCH_API_KEY",
            "GOOGLE_SEARCH_API_KEY",
        })
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static TaskCompletionSource<PendingUserDecision> CaptureNext(IUserDecisionBroker broker)
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

    private static async Task RunToCompletionAsync(
        AssistantService service,
        string message,
        CancellationToken cancellationToken = default)
    {
        await foreach (var unused in service.SendAsync(message, cancellationToken)) { }
    }

    private sealed class RecordingUserDecisionBroker : IUserDecisionBroker
    {
        private readonly UserDecisionBroker inner = CreateDecisionBroker();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> requestOwnerIds = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> cancelOwnerIds = new();

        public event EventHandler<PendingUserDecision>? PendingRequested
        {
            add => inner.PendingRequested += value;
            remove => inner.PendingRequested -= value;
        }

        public IReadOnlyList<string> RequestOwnerIds => requestOwnerIds.ToArray();

        public IReadOnlyList<string> CancelOwnerIds => cancelOwnerIds.ToArray();

        public Task<UserDecisionResult> RequestAsync(
            string ownerId,
            UserDecisionRequest request,
            CancellationToken cancellationToken)
        {
            requestOwnerIds.Enqueue(ownerId);
            return inner.RequestAsync(ownerId, request, cancellationToken);
        }

        public bool TryClaim(string requestId, string claimantId) =>
            inner.TryClaim(requestId, claimantId);

        public bool Release(string requestId, string claimantId) =>
            inner.Release(requestId, claimantId);

        public bool Submit(
            string requestId,
            string claimantId,
            IReadOnlyDictionary<string, object?> values,
            IReadOnlyDictionary<string, string>? customInputs = null) =>
            inner.Submit(requestId, claimantId, values, customInputs);

        public bool Cancel(string requestId, string claimantId, string reason) =>
            inner.Cancel(requestId, claimantId, reason);

        public int CancelOwner(string ownerId, string reason)
        {
            cancelOwnerIds.Enqueue(ownerId);
            return inner.CancelOwner(ownerId, reason);
        }

        public void Dispose() => inner.Dispose();
    }
    /// <summary>剧本式模型工厂：TryCreateAsync 返回预置客户端（或 null 模拟未配置）。</summary>
    private sealed class StubModelClientFactory(IModelClient? client)
        : AssistantModelClientFactory(
            new ThrowingHttpClientFactory(),
            null!,
            NullLogger<OpenAiCompatibleModelClient>.Instance,
            NullLogger<AssistantModelClientFactory>.Instance)
    {
        public override Task<IModelClient?> TryCreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(client);

        public override Task<AssistantModelInfo?> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AssistantModelInfo?>(client is null ? null : new AssistantModelInfo("stub", "stub-model", "https://stub.example.com/v1"));
    }

    private sealed class BlockingModelClientFactory(IModelClient client)
        : AssistantModelClientFactory(
            new ThrowingHttpClientFactory(),
            null!,
            NullLogger<OpenAiCompatibleModelClient>.Instance,
            NullLogger<AssistantModelClientFactory>.Instance)
    {
        public TaskCompletionSource<bool> FirstCreateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseFirstCreate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int createCount;

        public override async Task<IModelClient?> TryCreateAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref createCount) == 1)
            {
                FirstCreateStarted.TrySetResult(true);
                await ReleaseFirstCreate.Task.WaitAsync(cancellationToken);
            }

            return client;
        }
    }

    /// <summary>第二轮在首个正文增量后暂停，供测试精确模拟输出中切换会话。</summary>
    private sealed class SessionSwitchModelClient : IModelClient
    {
        private int turn;

        public TaskCompletionSource<bool> StreamingPaused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ContinueStreaming { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
            ModelRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref turn) == 1)
            {
                yield return new TextDeltaEvent("历史回答");
                yield return new ModelCompletedEvent("stop");
                yield break;
            }

            yield return new TextDeltaEvent("第一段");
            StreamingPaused.TrySetResult(true);
            await ContinueStreaming.Task.WaitAsync(cancellationToken);
            yield return new TextDeltaEvent("第二段");
            yield return new ModelCompletedEvent("stop");
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private sealed class FakeBrowserBridgeLifecycle : IBrowserBridgeLifecycle
    {
        public bool IsListening { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            IsListening = false;
            return Task.CompletedTask;
        }
    }}

