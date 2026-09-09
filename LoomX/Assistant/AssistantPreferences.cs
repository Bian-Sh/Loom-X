using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>修改权限模式：自动批准（写操作直接执行）或逐条批准（每次写操作都要用户确认）。</summary>
public enum AssistantPermissionMode
{
    AutoApprove,
    AskEachTime,
}

/// <summary>
/// 小助手用户偏好：选定的 Provider 模型（只走 Provider 模型，不走 combo）、
/// 思考等级与修改权限模式。持久化在应用数据目录，跨会话保留。
/// </summary>
public sealed record AssistantPreferences
{
    /// <summary>选定模型所属 Provider 的 BusinessId；null 表示自动选择。</summary>
    public string? ProviderBusinessId { get; init; }

    /// <summary>选定的模型 Id；null 表示自动选择。</summary>
    public string? ModelId { get; init; }

    /// <summary>思考等级：default（不下发）或 minimal/low/medium/high。</summary>
    public string ReasoningEffort { get; init; } = DefaultReasoningEffort;

    public AssistantPermissionMode PermissionMode { get; init; } = AssistantPermissionMode.AutoApprove;

    public const string DefaultReasoningEffort = "default";

    public static IReadOnlyList<string> ReasoningEfforts { get; } =
        [DefaultReasoningEffort, .. GatewayEndpointSettings.ReasoningEfforts];

    public static string NormalizeReasoningEffort(string? value) =>
        ReasoningEfforts.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!.Trim().ToLowerInvariant()
            : DefaultReasoningEffort;
}

/// <summary>
/// 偏好持久化：原子写 JSON，损坏时回落默认值而不是崩溃。
/// 只存安全元数据（标识与枚举），永不存 Secret。
/// </summary>
public sealed class AssistantPreferencesStore
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new() { WriteIndented = false };

    private readonly string filePath;
    private readonly ILogger<AssistantPreferencesStore>? logger;
    private readonly object sync = new();

    public AssistantPreferencesStore(string? filePath = null, ILogger<AssistantPreferencesStore>? logger = null)
    {
        this.filePath = filePath ?? Path.Combine(AppDataPaths.RootDirectory, "assistant-preferences.json");
        this.logger = logger;
    }

    public AssistantPreferences Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(filePath)) return new AssistantPreferences();
                var payload = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
                if (payload is null) return new AssistantPreferences();

                return new AssistantPreferences
                {
                    ProviderBusinessId = ReadString(payload, "provider_business_id"),
                    ModelId = ReadString(payload, "model_id"),
                    ReasoningEffort = AssistantPreferences.NormalizeReasoningEffort(ReadString(payload, "reasoning_effort")),
                    PermissionMode = Enum.TryParse<AssistantPermissionMode>(ReadString(payload, "permission_mode"), out var mode)
                        ? mode
                        : AssistantPermissionMode.AutoApprove,
                };
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger?.LogWarning(exception, "小助手偏好读取失败，使用默认值");
                return new AssistantPreferences();
            }
        }
    }

    public void Save(AssistantPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (sync)
        {
            var payload = new JsonObject
            {
                ["version"] = 1,
                ["provider_business_id"] = preferences.ProviderBusinessId,
                ["model_id"] = preferences.ModelId,
                ["reasoning_effort"] = AssistantPreferences.NormalizeReasoningEffort(preferences.ReasoningEffort),
                ["permission_mode"] = preferences.PermissionMode.ToString(),
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, payload.ToJsonString(StoreJsonOptions));
            File.Move(tempPath, filePath, overwrite: true);
        }
    }

    /// <summary>更新单项并落盘，返回更新后的偏好。</summary>
    public AssistantPreferences Update(Func<AssistantPreferences, AssistantPreferences> mutate)
    {
        var updated = mutate(Load());
        Save(updated);
        return updated;
    }

    private static string? ReadString(JsonObject payload, string key) =>
        payload[key]?.GetValue<string>() is { Length: > 0 } value ? value : null;
}
