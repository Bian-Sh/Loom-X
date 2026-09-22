using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>会话列表条目的安全摘要。</summary>
public sealed record AssistantSessionSummary(
    string SessionId,
    string Title,
    string State,
    DateTimeOffset UpdatedAt,
    int MessageCount)
{
    /// <summary>列表展示用的更新时间（本地时区）。</summary>
    public string DisplayUpdatedAt => UpdatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    /// <summary>标题是否来自用户自定义 / AI 摘要（true 时不再被自动标题覆盖）。</summary>
    public bool HasCustomTitle { get; init; }
}

/// <summary>
/// 会话持久化（规格 #17）：保存 Session/Messages/任务状态。
/// 这是内置助手自身的数据存储，不挂载 Router Plugin Pipeline。
/// </summary>
public sealed class AssistantSessionStore
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = false,
        // JSONL 是独立 UTF-8 文件，不需要为嵌入 HTML 转义中文；保留 JSON 必需的控制字符转义。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly string rootDirectory;
    private readonly ILogger<AssistantSessionStore>? logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> writeLocks = new();
    private readonly SemaphoreSlim titleLock = new(1, 1);

    public AssistantSessionStore(
        string? rootDirectory = null,
        ILogger<AssistantSessionStore>? logger = null)
    {
        this.rootDirectory = rootDirectory ?? Path.Combine(AppDataPaths.RootDirectory, "AssistantSessions");
        this.logger = logger;
    }

    /// <summary>
    /// 会话标题索引（sessionId → 标题）。与 jsonl 分开存，避免为了改一个标题去重写整个会话文件。
    /// 没有记录的会话回退到“首条用户消息截断”。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadTitlesAsync(CancellationToken cancellationToken = default)
    {
        var path = TitleIndexPath;
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        await titleLock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, StoreJsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception, "会话标题索引损坏，按空索引处理");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally { titleLock.Release(); }
    }

    /// <summary>写入会话标题；空标题表示清除（回到自动标题）。</summary>
    public async Task SetTitleAsync(string sessionId, string? title, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        await titleLock.WaitAsync(cancellationToken);
        try
        {
            var titles = File.Exists(TitleIndexPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(TitleIndexPath, cancellationToken), StoreJsonOptions)
                  ?? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(title)) titles.Remove(sessionId);
            else titles[sessionId] = title.Trim();

            var tempPath = TitleIndexPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(titles, StoreJsonOptions), cancellationToken);
            File.Move(tempPath, TitleIndexPath, overwrite: true);
        }
        finally { titleLock.Release(); }
    }

    private string TitleIndexPath => Path.Combine(rootDirectory, "titles.json");

    public async Task SaveAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(rootDirectory);

        // v2 首次保存使用完整快照，之后追加生命周期记录；消息本身不逐 token 落盘。
        var lines = new List<string>
        {
            new JsonObject
            {
                ["type"] = "session",
                ["version"] = 2,
                ["session_id"] = session.Id,
            }.ToJsonString(StoreJsonOptions),
        };

        foreach (var message in session.Messages)
        {
            lines.Add(new JsonObject
            {
                ["type"] = "message",
                ["id"] = message.Id,
                ["timestamp"] = message.Timestamp,
                ["role"] = message.Role.ToString(),
                ["content"] = message.Content,
                ["tool_name"] = message.ToolName,
                ["tool_call_id"] = message.ToolCallId,
                ["tool_calls"] = message.ToolCalls.Count == 0
                    ? null
                    : new JsonArray(message.ToolCalls.Select(call => (JsonNode?)new JsonObject
                    {
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["arguments"] = ToolCallProjection.EnsureSafe(call).ArgumentsJson,
                        ["arguments_safe"] = true,
                    }).ToArray()),
                ["blocks"] = message.Blocks.Count == 0 ? null : new JsonArray(message.Blocks.Select(block => (JsonNode?)new JsonObject
                {
                    ["kind"] = block.Kind.ToString(),
                    ["text"] = block.Text,
                    ["summary"] = block.IsSummary,
                    ["tool_call"] = block.ToolCall is null ? null : new JsonObject
                    {
                        ["id"] = block.ToolCall.Id,
                        ["name"] = block.ToolCall.Name,
                        ["arguments"] = ToolCallProjection.EnsureSafe(block.ToolCall).ArgumentsJson,
                        ["arguments_safe"] = true,
                    },
                }).ToArray()),
            }.ToJsonString(StoreJsonOptions));
        }

        foreach (var activity in session.Activities.Where(item => item.Kind is not (AgentEventKind.TextDelta or AgentEventKind.ReasoningDelta or AgentEventKind.MessageCompleted)))
        {
            lines.Add(new JsonObject
            {
                ["type"] = "custom",
                ["id"] = activity.Id,
                ["timestamp"] = activity.Timestamp,
                ["kind"] = activity.Kind.ToString(),
                ["step"] = activity.Step,
                ["detail"] = activity.Kind == AgentEventKind.ToolApprovalRequested ? null : activity.Detail,
                ["tool_name"] = activity.ToolName,
                ["tool_call_id"] = activity.ToolCallId,
                ["success"] = activity.Success,
            }.ToJsonString(StoreJsonOptions));
        }

        lines.Add(new JsonObject
        {
            ["type"] = "custom",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["kind"] = "State",
            ["state"] = session.State.ToString(),
        }.ToJsonString(StoreJsonOptions));

        var path = PathFor(session.Id);
        var gate = writeLocks.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
            var valid = new List<JsonObject>();
            foreach (var line in existing)
            {
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject item) break;
                    valid.Add(item);
                }
                catch (JsonException) { break; }
            }

            var isV2 = valid.FirstOrDefault()?["type"]?.GetValue<string>() == "session";
            var knownIds = isV2 ? valid.Select(item => item["id"]?.GetValue<string>())
                .Where(id => id is not null).ToHashSet(StringComparer.Ordinal) : [];
            var previousState = valid.LastOrDefault(item => item["kind"]?.GetValue<string>() == "State")?["state"]?.GetValue<string>();
            var pending = (isV2 ? lines.Skip(1) : lines.AsEnumerable())
                .Where((line, index) => !isV2 || !knownIds.Contains(JsonNode.Parse(line)!["id"]!.GetValue<string>()))
                .ToList();
            if (isV2 && previousState == session.State.ToString()) pending.RemoveAt(pending.Count - 1);
            if (pending.Count == 0 && valid.Count == existing.Length) return;

            var parentId = valid.LastOrDefault()?["id"]?.GetValue<string>();
            for (var index = isV2 ? 0 : 1; index < pending.Count; index++)
            {
                var item = JsonNode.Parse(pending[index])!.AsObject();
                item["parent_id"] = parentId;
                parentId = item["id"]?.GetValue<string>();
                pending[index] = item.ToJsonString(StoreJsonOptions);
            }

            var append = string.Join('\n', pending) + '\n';

            if (isV2 && valid.Count == existing.Length)
                await File.AppendAllTextAsync(path, append, cancellationToken);
            else
            {
                var prefix = isV2 ? string.Join('\n', existing.Take(valid.Count)) + '\n' : string.Empty;
                var tempPath = path + ".tmp";
                await File.WriteAllTextAsync(tempPath, prefix + append, cancellationToken);
                File.Move(tempPath, path, overwrite: true);
            }
        }
        finally { gate.Release(); }
    }

    public async Task<AgentSession?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path)) return null;

        JsonObject? meta = null;
        var session = new AgentSession();
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? item;
            try { item = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { break; }
            if (item is null) break;

            var type = item["type"]?.GetValue<string>();
            if (type is "meta" or "session")
            {
                meta = item;
                session.RestoreId(item["session_id"]!.GetValue<string>());
                if (item["state"]?.GetValue<string>() is { } initialState)
                    session.RestoreState(Enum.Parse<AgentSessionState>(initialState));
                continue;
            }

            if (type == "custom")
            {
                if (item["kind"]?.GetValue<string>() == "State")
                {
                    if (item["state"]?.GetValue<string>() is { } state)
                        session.RestoreState(Enum.Parse<AgentSessionState>(state));
                }
                else if (Enum.TryParse<AgentEventKind>(item["kind"]?.GetValue<string>(), out var kind))
                    session.RecordActivity(new AgentEvent(session.Id, kind,
                        item["timestamp"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UtcNow)
                    {
                        Id = item["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                        Step = item["step"]?.GetValue<int>(),
                        Detail = item["detail"]?.GetValue<string>(),
                        ToolName = item["tool_name"]?.GetValue<string>(),
                        ToolCallId = item["tool_call_id"]?.GetValue<string>(),
                        Success = item["success"]?.GetValue<bool>(),
                    });
                continue;
            }

            if (type is not "message" && item["role"] is null) continue;
            var role = Enum.Parse<ChatRole>(item["role"]!.GetValue<string>());
            var content = item["content"]?.GetValue<string>();
            var toolCalls = item["tool_calls"]?.AsArray()
                .Select(call => new ToolCall(
                    call!["id"]!.GetValue<string>(),
                    call["name"]!.GetValue<string>(),
                    call["arguments"]!.GetValue<string>())
                {
                    ArgumentsAreSafe = call["arguments_safe"]?.GetValue<bool>() == true,
                })
                .ToArray() ?? [];
            var blocks = item["blocks"]?.AsArray().Select(block => new ChatContentBlock(
                Enum.Parse<ChatContentKind>(block!["kind"]!.GetValue<string>()),
                block["text"]?.GetValue<string>(),
                block["summary"]?.GetValue<bool>() ?? false,
                block["tool_call"] is JsonObject callNode ? new ToolCall(callNode["id"]!.GetValue<string>(), callNode["name"]!.GetValue<string>(), callNode["arguments"]!.GetValue<string>())
                {
                    ArgumentsAreSafe = callNode["arguments_safe"]?.GetValue<bool>() == true,
                } : null)).ToArray() ?? [];
            session.RestoreMessage(new ChatMessage(role, content)
            {
                Id = item["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                Timestamp = item["timestamp"]?.GetValue<DateTimeOffset>() ?? meta?["updated_at"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UtcNow,
                ToolCalls = toolCalls,
                Blocks = blocks,
                ToolName = item["tool_name"]?.GetValue<string>(),
                ToolCallId = item["tool_call_id"]?.GetValue<string>(),
            });
        }

        if (meta is null) return null;
        logger?.LogDebug("AI 助手会话已恢复 {SessionId} 消息数 {MessageCount}", session.Id, session.Messages.Count);
        return session;
    }

    public IReadOnlyList<AssistantSessionSummary> List()
    {
        if (!Directory.Exists(rootDirectory)) return [];
        var titles = File.Exists(TitleIndexPath)
            ? SafeLoadTitles()
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var summaries = new List<AssistantSessionSummary>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.jsonl"))
        {
            try
            {
                JsonObject? meta = null;
                string? state = null;
                var updatedAt = DateTimeOffset.MinValue;
                string? firstUserContent = null;
                var messageCount = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonObject? item;
                    try { item = JsonNode.Parse(line) as JsonObject; }
                    catch (JsonException) { break; }
                    if (item is null) break;

                    if (item["type"]?.GetValue<string>() is "meta" or "session")
                    {
                        meta = item;
                        state = item["state"]?.GetValue<string>();
                        updatedAt = item["updated_at"]?.GetValue<DateTimeOffset>() ?? updatedAt;
                        continue;
                    }

                    updatedAt = item["timestamp"]?.GetValue<DateTimeOffset>() ?? updatedAt;
                    if (item["kind"]?.GetValue<string>() == "State") state = item["state"]?.GetValue<string>();

                    if (item["type"]?.GetValue<string>() != "message") continue;
                    messageCount++;
                    if (firstUserContent is null
                        && item["role"]?.GetValue<string>() == nameof(ChatRole.User))
                    {
                        firstUserContent = item["content"]?.GetValue<string>();
                    }
                }

                if (meta is null) continue;
                var sessionId = meta["session_id"]!.GetValue<string>();
                var hasCustomTitle = titles.TryGetValue(sessionId, out var customTitle)
                                     && !string.IsNullOrWhiteSpace(customTitle);
                summaries.Add(new AssistantSessionSummary(
                    sessionId,
                    hasCustomTitle ? customTitle! : Truncate(Flatten(firstUserContent) ?? "（空会话）", 40),
                    state == nameof(AgentSessionState.Running) ? nameof(AgentSessionState.Cancelled) : state ?? nameof(AgentSessionState.Created),
                    updatedAt,
                    messageCount)
                {
                    HasCustomTitle = hasCustomTitle,
                });
            }
            catch (JsonException)
            {
                // 损坏的会话文件跳过
            }
        }

        return summaries.OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    public void Delete(string sessionId)
    {
        var path = PathFor(sessionId);
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(TitleIndexPath))
        {
            var titles = SafeLoadTitles();
            if (titles.Remove(sessionId)) _ = SetTitleAsync(sessionId, null);
        }
    }

    private string PathFor(string sessionId) => Path.Combine(rootDirectory, $"{sessionId}.jsonl");

    private Dictionary<string, string> SafeLoadTitles()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TitleIndexPath), StoreJsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger?.LogWarning(exception, "会话标题索引读取失败，按空索引处理");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>标题只取一行：折叠换行/多余空白，避免长粘贴把列表撑成多行。</summary>
    private static string? Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var collapsed = string.Join(' ', text.Split('\n', '\r', '\t')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
