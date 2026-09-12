using System.Text.Json;
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
    int MessageCount);

/// <summary>
/// 会话持久化（规格 #17）：保存 Session/Messages/任务状态，
/// 消息内容本身已遵守 Secret 边界（只有 secret_ref），持久化层不再二次过滤，
/// 但写入前做一次兜底扫描，发现疑似 Secret 形态的值拒绝落盘。
/// </summary>
public sealed class AssistantSessionStore
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new() { WriteIndented = false };

    private readonly string rootDirectory;
    private readonly ILogger<AssistantSessionStore>? logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> writeLocks = new();

    public AssistantSessionStore(string? rootDirectory = null, ILogger<AssistantSessionStore>? logger = null)
    {
        this.rootDirectory = rootDirectory ?? Path.Combine(AppDataPaths.RootDirectory, "AssistantSessions");
        this.logger = logger;
    }

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
                        ["arguments"] = call.ArgumentsJson,
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
                        ["arguments"] = block.ToolCall.ArgumentsJson,
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
            if (SecretLeakScan(append))
            {
                logger?.LogError("AI 助手会话 {SessionId} 检出疑似 Secret，已拒绝落盘", session.Id);
                throw new InvalidOperationException("会话内容检出疑似 Secret，已拒绝保存。");
            }

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
                    call["arguments"]!.GetValue<string>()))
                .ToArray() ?? [];
            var blocks = item["blocks"]?.AsArray().Select(block => new ChatContentBlock(
                Enum.Parse<ChatContentKind>(block!["kind"]!.GetValue<string>()),
                block["text"]?.GetValue<string>(),
                block["summary"]?.GetValue<bool>() ?? false,
                block["tool_call"] is JsonObject callNode ? new ToolCall(callNode["id"]!.GetValue<string>(), callNode["name"]!.GetValue<string>(), callNode["arguments"]!.GetValue<string>()) : null)).ToArray() ?? [];
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
                summaries.Add(new AssistantSessionSummary(
                    meta["session_id"]!.GetValue<string>(),
                    Truncate(firstUserContent ?? "（空会话）", 40),
                    state == nameof(AgentSessionState.Running) ? nameof(AgentSessionState.Cancelled) : state ?? nameof(AgentSessionState.Created),
                    updatedAt,
                    messageCount));
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
    }

    private string PathFor(string sessionId) => Path.Combine(rootDirectory, $"{sessionId}.jsonl");

    /// <summary>兜底扫描：sk-/Bearer 形态的长值不允许落盘。</summary>
    internal static bool SecretLeakScan(string json)
    {
        var span = json.AsSpan();
        return span.Contains("sk-", StringComparison.Ordinal) && ScanForKeyShape(span)
            || span.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) && ScanForBearerShape(span);
    }

    private static bool ScanForKeyShape(ReadOnlySpan<char> text)
    {
        var index = text.IndexOf("sk-", StringComparison.Ordinal);
        while (index >= 0)
        {
            var tail = text[(index + 3)..];
            var length = 0;
            foreach (var character in tail)
            {
                if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.') length++;
                else break;
            }

            if (length >= 20) return true;
            index = text[(index + 3)..].IndexOf("sk-", StringComparison.Ordinal) is var next && next >= 0 ? index + 3 + next : -1;
        }

        return false;
    }

    private static bool ScanForBearerShape(ReadOnlySpan<char> text)
    {
        var index = text.IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var tail = text[(index + 7)..];
            var length = 0;
            foreach (var character in tail)
            {
                if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.') length++;
                else break;
            }

            if (length >= 16) return true;
            index = text[(index + 7)..].IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase) is var next && next >= 0 ? index + 7 + next : -1;
        }

        return false;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
