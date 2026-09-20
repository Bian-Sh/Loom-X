using LoomX.Assistant.UserDecisions;
using LoomX.Assistant.Browser;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>待用户批准的修改操作摘要（只含安全摘要，参数已截断）。</summary>
public sealed record ToolApprovalRequest(string ToolCallId, string ToolName, string ArgumentsSummary);

/// <summary>
/// 小助手会话门面：管理 AgentSession 生命周期、驱动 AgentLoop、向外暴露 AgentEvent 流。
/// UI 只与本门面与 AgentEvent 打交道，不接触 Harness 内部对象（规格 #15）。
/// 系统提示词在这里统一定义，承载工具使用与安全约束。
/// </summary>
public sealed class AssistantService
{
    private const string SystemPrompt = """
        你是 LoomX 内置的 AI 助手，帮助用户配置与诊断 LoomX（AI 网关/路由器）。
        规则：
        1. 配置类操作遵循：读取 → 备份 → 修改 → 验证 → 测试，不要跳步。
        2. 涉及中转站/Provider/模型概念时先用 skill.list / skill.load 加载对应 Skill 再行动。
        3. API Key 永远以 secret_ref 形式出现是正常的，不要向用户索要明文，也不要试图拼出明文。
        4. 高风险不可逆操作时打断用户；普通内部步骤默认不额外询问，但这只是避免打扰，不是 assistant.ask_user 的能力限制。
        5. assistant.ask_user 是通用 Human-in-the-loop 工具，可用于用户主动测试、偏好收集、必要输入、歧义澄清和行动确认；用户明确要求测试 AskUser 时直接调用，不需要加载 Skill，也不需要 Browser Bridge 或 Chrome。
        6. 回答使用中文，简洁直接，配置结果用要点列出。
        7. 资料顺序：优先使用模型原生或已有的官方资料能力；其次用 Browser Bridge 的 browser.open、browser.read、browser.wait 读取用户授权页面；无可用通道时用 assistant.ask_user 请求用户提供资料或结论。
        8. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge，立即暂停并交还用户；禁止绕过网站安全机制。
        """;

    private readonly AssistantModelClientFactory modelClientFactory;
    private readonly ToolRegistry toolRegistry;
    private readonly AssistantSessionStore sessionStore;
    private readonly AssistantPreferencesStore? preferencesStore;
    private readonly IUserDecisionBroker userDecisionBroker;
    private readonly BrowserBridgeLeaseManager? browserBridgeLeaseManager;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AssistantService> logger;
    private readonly SemaphoreSlim runLock = new(1, 1);

    private CancellationTokenSource? currentRun;
    private string? currentRunOwnerId;

    public AssistantService(
        AssistantModelClientFactory modelClientFactory,
        ToolRegistry toolRegistry,
        AssistantSessionStore sessionStore,
        ILoggerFactory loggerFactory,
        AssistantPreferencesStore? preferencesStore,
        IUserDecisionBroker userDecisionBroker,
        BrowserBridgeLeaseManager? browserBridgeLeaseManager = null)
    {
        this.modelClientFactory = modelClientFactory;
        this.toolRegistry = toolRegistry;
        this.sessionStore = sessionStore;
        this.loggerFactory = loggerFactory;
        this.preferencesStore = preferencesStore;
        this.userDecisionBroker = userDecisionBroker;
        this.browserBridgeLeaseManager = browserBridgeLeaseManager;
        logger = loggerFactory.CreateLogger<AssistantService>();
        CurrentSession = CreateSession();
    }

    public AgentSession CurrentSession { get; private set; }

    public bool IsRunning => CurrentSession.State == AgentSessionState.Running;

    private static AgentSession CreateSession()
    {
        var session = new AgentSession(new AgentSessionOptions { MaxSteps = 16 });
        session.ApplySystemPrompt(BuildSystemPrompt(session.Id));
        return session;
    }

    private static string BuildSystemPrompt(string assistantSessionId) => $"""
        {SystemPrompt}
        8. 当前 Assistant Session ID：{assistantSessionId}。需要浏览器时先调用 browser.bridge_start，并把此 ID 作为 assistant_session_id；完成本 Session 的全部浏览器操作后主动调用 browser.bridge_stop 释放同一 ID。不得使用或释放其他 Session ID。新建、切换、离开或关闭 Session UI 不会自动释放租约；删除 Session 仅清理意外残留。
        """;
    /// <summary>助手模型是否可用（安全摘要，不含 Key）。</summary>
    public async Task<AssistantModelInfo?> DescribeModelAsync(CancellationToken cancellationToken = default) =>
        await modelClientFactory.DescribeAsync(cancellationToken);

    /// <summary>可选助手模型（按 Provider 分组，只走 Provider 模型，不走 combo）。</summary>
    public Task<IReadOnlyList<AssistantProviderModelGroup>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        modelClientFactory.ListAvailableAsync(cancellationToken);

    /// <summary>当前指定模型；首次使用前为空，此时使用可用列表首个模型。</summary>
    public (string? ProviderBusinessId, string? ModelId) GetModelSelection() =>
        modelClientFactory.GetPreferredSelection();

    /// <summary>指定助手模型（持久化，下一次发送生效）。</summary>
    public void SelectModel(string providerBusinessId, string modelId) =>
        modelClientFactory.SetPreferredSelection(providerBusinessId, modelId);

    /// <summary>清除模型偏好；下一次使用时重新采用可用列表首个模型。</summary>
    public void ClearModelSelection() => modelClientFactory.ClearPreferredSelection();

    /// <summary>修改权限模式（自动批准/逐条批准），持久化。</summary>
    public AssistantPermissionMode PermissionMode
    {
        get => preferencesStore?.Load().PermissionMode ?? AssistantPermissionMode.AutoApprove;
        set => preferencesStore?.Update(current => current with { PermissionMode = value });
    }

    /// <summary>思考等级：default（不下发）或 minimal/low/medium/high，持久化。</summary>
    public string ReasoningEffort
    {
        get => AssistantPreferences.NormalizeReasoningEffort(preferencesStore?.Load().ReasoningEffort);
        set => preferencesStore?.Update(current => current with { ReasoningEffort = AssistantPreferences.NormalizeReasoningEffort(value) });
    }

    /// <summary>
    /// 逐条批准模式下的 UI 审批桥：返回 true 批准执行。
    /// 由 UI 在发送前挂接；未挂接时默认批准并记录日志（无 UI 的宿主不会静默卡住）。
    /// </summary>
    public Func<ToolApprovalRequest, Task<bool>>? ApprovalHandler { get; set; }

    /// <summary>历史会话列表。</summary>
    public IReadOnlyList<AssistantSessionSummary> ListSessions() => sessionStore.List();

    /// <summary>新建空会话并设为当前。</summary>
    public AgentSession NewSession()
    {
        Cancel();
        CurrentSession = CreateSession();
        return CurrentSession;
    }

    /// <summary>载入历史会话并设为当前；不存在时返回 false。</summary>
    public async Task<bool> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.LoadAsync(sessionId, cancellationToken);
        if (session is null) return false;
        session.ApplySystemPrompt(BuildSystemPrompt(session.Id));
        CurrentSession = session;
        return true;
    }

    /// <summary>删除会话；租约释放仅作为意外残留的被动兜底。</summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (browserBridgeLeaseManager is not null)
        {
            try
            {
                await browserBridgeLeaseManager.ReleaseAsync(sessionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "删除 AI 助手会话时清理 Browser Bridge 残留租约失败 {SessionId}", sessionId);
            }
        }

        sessionStore.Delete(sessionId);
    }

    /// <summary>重命名历史会话（空值表示回到自动标题）。</summary>
    public Task RenameSessionAsync(string sessionId, string? title, CancellationToken cancellationToken = default) =>
        sessionStore.SetTitleAsync(sessionId, title, cancellationToken);

    /// <summary>取消当前运行及其等待中的用户决策。</summary>
    public void Cancel()
    {
        var ownerId = currentRunOwnerId;
        currentRun?.Cancel();
        if (ownerId is not null)
        {
            userDecisionBroker.CancelOwner(ownerId, "assistant_run_cancelled");
        }
    }

    /// <summary>
    /// 用助手模型给当前会话起一个短标题（≤ 20 字）。
    /// 只在会话还没有自定义/摘要标题时调用；失败一律静默（标题是锦上添花，不该打扰用户）。
    /// </summary>
    public async Task<string?> TrySummarizeTitleAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSession.Id;
        var firstUser = CurrentSession.Messages.FirstOrDefault(item => item.Role == ChatRole.User)?.Content;
        var firstAnswer = CurrentSession.Messages.FirstOrDefault(item => item.Role == ChatRole.Assistant)?.Content;
        if (string.IsNullOrWhiteSpace(firstUser) || string.IsNullOrWhiteSpace(firstAnswer)) return null;

        var modelClient = await modelClientFactory.TryCreateAsync(cancellationToken);
        if (modelClient is null) return null;

        var prompt = $"""
            给下面这段对话起一个中文标题，要求：不超过 20 个字、只输出标题本身、不要引号和标点结尾、不要复述用户原话。

            用户：{Trim(firstUser, 400)}
            助手：{Trim(firstAnswer, 400)}
            """;

        var request = new ModelRequest(
            [new ChatMessage(ChatRole.User, prompt)],
            Array.Empty<ToolDefinition>());

        var builder = new System.Text.StringBuilder();
        await foreach (var streamEvent in modelClient.StreamAsync(request, cancellationToken))
        {
            if (streamEvent is TextDeltaEvent delta) builder.Append(delta.Text);
            else if (streamEvent is ModelCompletedEvent) break;
        }

        var title = NormalizeTitle(builder.ToString());
        if (string.IsNullOrEmpty(title)) return null;

        // 用捕获的 sessionId 写回：await 期间用户可能切了会话
        await sessionStore.SetTitleAsync(sessionId, title, cancellationToken);
        logger.LogInformation("已为会话 {SessionId} 生成摘要标题", sessionId);
        return title;
    }

    private static string Trim(string text, int maxLength)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= maxLength ? flat : flat[..maxLength] + "…";
    }

    /// <summary>模型输出可能带引号、前缀或整段复述，这里只取第一行并裁到 20 字。</summary>
    internal static string? NormalizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var line = raw.Split('\n', '\r').FirstOrDefault(item => item.Trim().Length > 0);
        if (line is null) return null;
        var title = line.Trim().Trim('"', '\'', '“', '”', '《', '》', '。', '：', ':', ' ');
        if (title.Length == 0) return null;
        return title.Length <= 20 ? title : title[..20];
    }

    private ToolApprovalGate BuildApprovalGate() => async (toolCall, tool, cancellationToken) =>
    {
        var handler = ApprovalHandler;
        if (handler is null)
        {
            logger.LogWarning("逐条批准模式但未挂接审批 UI，默认放行 {ToolName}", tool.Name);
            return true;
        }

        return await handler(new ToolApprovalRequest(toolCall.Id, tool.Name, Summarize(toolCall.ArgumentsJson)));
    };

    private static string Summarize(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return string.Empty;
        var compact = argumentsJson.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }

    /// <summary>
    /// 发送用户消息并驱动一轮 Agent 循环，事件通过返回值逐条流出。
    /// 助手模型未配置时抛出 InvalidOperationException（UI 应提示用户先配置模型）。
    /// </summary>
    public async IAsyncEnumerable<AgentEvent> SendAsync(
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        if (!runLock.Wait(0))
        {
            throw new InvalidOperationException("AI 助手正在运行中，请先等待或取消。");
        }

        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("AI 助手正在运行中，请先等待或取消。");
            }

            var runSession = CurrentSession;
            var ownerId = Guid.NewGuid().ToString("N");
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            currentRun = runCancellation;
            currentRunOwnerId = ownerId;
            var persistenceBlocked = false;

            try
            {
                var modelClient = await modelClientFactory.TryCreateAsync(runCancellation.Token);
                if (modelClient is null)
                {
                    throw new InvalidOperationException("AI 助手模型未配置。请在 LoomX 中启用一个 openai 兼容的 Provider 与模型。");
                }

                var approvalGate = PermissionMode == AssistantPermissionMode.AskEachTime
                    ? BuildApprovalGate()
                    : null;
                var loop = new AgentLoop(modelClient, toolRegistry, loggerFactory.CreateLogger<AgentLoop>(), approvalGate,
                    ModelErrorFormatter.FormatException, ModelErrorFormatter.FormatMaxStepsExceeded);
                await using var enumerator = loop
                    .RunAsync(runSession, userMessage, runCancellation.Token)
                    .GetAsyncEnumerator(runCancellation.Token);
                while (true)
                {
                    AgentEvent agentEvent;
                    using (AssistantTools.BeginRun(ownerId))
                    {
                        if (!await enumerator.MoveNextAsync())
                        {
                            break;
                        }

                        agentEvent = enumerator.Current;
                    }

                    if (agentEvent.Kind is not (AgentEventKind.TextDelta or AgentEventKind.ReasoningDelta or AgentEventKind.MessageCompleted))
                        runSession.RecordActivity(agentEvent);
                    if (!persistenceBlocked && agentEvent.Kind is not (AgentEventKind.TextDelta or AgentEventKind.ReasoningDelta))
                    {
                        var failedToSave = false;
                        try { await sessionStore.SaveAsync(runSession, CancellationToken.None); }
                        catch (Exception exception)
                        {
                            persistenceBlocked = true;
                            failedToSave = true;
                            logger.LogError(exception, "AI 助手会话无法继续保存 {SessionId}", runSession.Id);
                        }
                        if (failedToSave) yield return AgentEvent.Create(runSession.Id, AgentEventKind.PersistenceFailed);
                    }
                    yield return agentEvent;
                }
            }
            finally
            {
                userDecisionBroker.CancelOwner(ownerId, "assistant_run_cancelled");
                currentRun = null;
                currentRunOwnerId = null;

                try
                {
                    if (!persistenceBlocked) await sessionStore.SaveAsync(runSession, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "AI 助手会话保存失败 {SessionId}", runSession.Id);
                }
            }
        }
        finally
        {
            runLock.Release();
        }
    }
}
