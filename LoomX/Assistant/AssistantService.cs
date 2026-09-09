using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 小助手会话门面：管理 AgentSession 生命周期、驱动 AgentLoop、向外暴露 AgentEvent 流。
/// UI 只与本门面与 AgentEvent 打交道，不接触 Harness 内部对象（规格 #15）。
/// 系统提示词在这里统一定义，承载工具使用与安全约束。
/// </summary>
public sealed class AssistantService
{
    private const string SystemPrompt = """
        你是 LoomX 内置的小助手，帮助用户配置与诊断 LoomX（AI 网关/路由器）。
        规则：
        1. 配置类操作遵循：读取 → 备份 → 修改 → 验证 → 测试，不要跳步。
        2. 涉及中转站/Provider/模型概念时先用 skill.list / skill.load 加载对应 Skill 再行动。
        3. API Key 永远以 secret_ref 形式出现是正常的，不要向用户索要明文，也不要试图拼出明文。
        4. 登录、CAPTCHA、2FA、高风险不可逆操作时才打断用户；正常步骤不要逐步询问。
        5. 回答使用中文，简洁直接，配置结果用要点列出。
        """;

    private readonly AssistantModelClientFactory modelClientFactory;
    private readonly ToolRegistry toolRegistry;
    private readonly AssistantSessionStore sessionStore;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AssistantService> logger;
    private readonly SemaphoreSlim runLock = new(1, 1);

    private CancellationTokenSource? currentRun;

    public AssistantService(
        AssistantModelClientFactory modelClientFactory,
        ToolRegistry toolRegistry,
        AssistantSessionStore sessionStore,
        ILoggerFactory loggerFactory)
    {
        this.modelClientFactory = modelClientFactory;
        this.toolRegistry = toolRegistry;
        this.sessionStore = sessionStore;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<AssistantService>();
    }

    public AgentSession CurrentSession { get; private set; } = new(new AgentSessionOptions
    {
        MaxSteps = 16,
        SystemPrompt = SystemPrompt,
    });

    public bool IsRunning => CurrentSession.State == AgentSessionState.Running;

    /// <summary>助手模型是否可用（安全摘要，不含 Key）。</summary>
    public async Task<AssistantModelInfo?> DescribeModelAsync(CancellationToken cancellationToken = default) =>
        await modelClientFactory.DescribeAsync(cancellationToken);

    /// <summary>历史会话列表。</summary>
    public IReadOnlyList<AssistantSessionSummary> ListSessions() => sessionStore.List();

    /// <summary>新建空会话并设为当前。</summary>
    public AgentSession NewSession()
    {
        CurrentSession = new AgentSession(new AgentSessionOptions
        {
            MaxSteps = 16,
            SystemPrompt = SystemPrompt,
        });
        return CurrentSession;
    }

    /// <summary>载入历史会话并设为当前；不存在时返回 false。</summary>
    public async Task<bool> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.LoadAsync(sessionId, cancellationToken);
        if (session is null) return false;
        CurrentSession = session;
        return true;
    }

    public void DeleteSession(string sessionId) => sessionStore.Delete(sessionId);

    /// <summary>取消当前运行。</summary>
    public void Cancel() => currentRun?.Cancel();

    /// <summary>
    /// 发送用户消息并驱动一轮 Agent 循环，事件通过返回值逐条流出。
    /// 助手模型未配置时抛出 InvalidOperationException（UI 应提示用户先配置模型）。
    /// </summary>
    public async IAsyncEnumerable<AgentEvent> SendAsync(
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        if (IsRunning)
        {
            throw new InvalidOperationException("小助手正在运行中，请先等待或取消。");
        }

        var modelClient = await modelClientFactory.TryCreateAsync(cancellationToken);
        if (modelClient is null)
        {
            throw new InvalidOperationException("小助手模型未配置。请在 LoomX 中启用一个 openai 兼容的 Provider 与模型。");
        }

        await runLock.WaitAsync(cancellationToken);
        currentRun = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var loop = new AgentLoop(modelClient, toolRegistry, loggerFactory.CreateLogger<AgentLoop>());
            await foreach (var agentEvent in loop.RunAsync(CurrentSession, userMessage, currentRun.Token))
            {
                yield return agentEvent;
            }
        }
        finally
        {
            currentRun.Dispose();
            currentRun = null;
            runLock.Release();

            try
            {
                await sessionStore.SaveAsync(CurrentSession, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "小助手会话保存失败 {SessionId}", CurrentSession.Id);
            }
        }
    }
}
