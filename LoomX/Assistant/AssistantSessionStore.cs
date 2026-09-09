using System.Text.Json;
using System.Text.Json.Nodes;
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

    public AssistantSessionStore(string? rootDirectory = null, ILogger<AssistantSessionStore>? logger = null)
    {
        this.rootDirectory = rootDirectory ?? Path.Combine(AppDataPaths.RootDirectory, "AssistantSessions");
        this.logger = logger;
    }

    public async Task SaveAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(rootDirectory);

        // JSON Lines 格式：第一行 meta，后续每行一条消息。
        var lines = new List<string>
        {
            new JsonObject
            {
                ["type"] = "meta",
                ["version"] = 1,
                ["session_id"] = session.Id,
                ["state"] = session.State.ToString(),
                ["updated_at"] = DateTimeOffset.UtcNow,
            }.ToJsonString(StoreJsonOptions),
        };

        foreach (var message in session.Messages)
        {
            lines.Add(new JsonObject
            {
                ["type"] = "message",
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
            }.ToJsonString(StoreJsonOptions));
        }

        var jsonl = string.Join('\n', lines) + '\n';
        if (SecretLeakScan(jsonl))
        {
            logger?.LogError("小助手会话 {SessionId} 检出疑似 Secret，已拒绝落盘", session.Id);
            throw new InvalidOperationException("会话内容检出疑似 Secret，已拒绝保存。");
        }

        // 原子写：先写临时文件再替换
        var path = PathFor(session.Id);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, jsonl, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
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
            if (JsonNode.Parse(line) is not JsonObject item) continue;

            if (item["type"]?.GetValue<string>() == "meta")
            {
                meta = item;
                session.RestoreId(item["session_id"]!.GetValue<string>());
                session.RestoreState(Enum.Parse<AgentSessionState>(item["state"]!.GetValue<string>()));
                continue;
            }

            var role = Enum.Parse<ChatRole>(item["role"]!.GetValue<string>());
            var content = item["content"]?.GetValue<string>();
            var toolCalls = item["tool_calls"]?.AsArray()
                .Select(call => new ToolCall(
                    call!["id"]!.GetValue<string>(),
                    call["name"]!.GetValue<string>(),
                    call["arguments"]!.GetValue<string>()))
                .ToArray() ?? [];
            session.RestoreMessage(new ChatMessage(role, content)
            {
                ToolCalls = toolCalls,
                ToolName = item["tool_name"]?.GetValue<string>(),
                ToolCallId = item["tool_call_id"]?.GetValue<string>(),
            });
        }

        if (meta is null) return null;
        logger?.LogDebug("小助手会话已恢复 {SessionId} 消息数 {MessageCount}", session.Id, session.Messages.Count);
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
                string? firstUserContent = null;
                var messageCount = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (JsonNode.Parse(line) is not JsonObject item) continue;

                    if (item["type"]?.GetValue<string>() == "meta")
                    {
                        meta = item;
                        continue;
                    }

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
                    meta["state"]!.GetValue<string>(),
                    meta["updated_at"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.MinValue,
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
