using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
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
/// 思考等级与修改权限模式。持久化在配置库（LoomX.db）的 AssistantPreferences 单行表，跨会话保留。
/// </summary>
public sealed record AssistantPreferences
{
    /// <summary>选定模型所属 Provider 的 BusinessId；首次使用前为空。</summary>
    public string? ProviderBusinessId { get; init; }

    /// <summary>选定的模型 Id；首次使用前为空。</summary>
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

    internal static string NormalizePermissionMode(AssistantPermissionMode mode) => mode switch
    {
        AssistantPermissionMode.AskEachTime => nameof(AssistantPermissionMode.AskEachTime),
        _ => nameof(AssistantPermissionMode.AutoApprove),
    };
}

/// <summary>
/// 偏好持久化：读写配置库 LoomX.db 的 AssistantPreferences 单行表（Id=1）。
/// 损坏/缺表时回落默认值而不是崩溃。只存安全元数据（标识与枚举），永不存 Secret。
/// 接口保持同步（调用方在 UI/服务线程直接读取，无 async 上下文）。
/// </summary>
public sealed class AssistantPreferencesStore
{
    private readonly IDbContextFactory<ConfigurationDbContext> dbContextFactory;
    private readonly ILogger<AssistantPreferencesStore>? logger;
    private readonly object sync = new();

    /// <summary>旧版 JSON 文件路径；存在时作为一次性迁移源（迁移后不再使用）。</summary>
    internal static string LegacyJsonPath => System.IO.Path.Combine(AppDataPaths.RootDirectory, "assistant-preferences.json");

    public AssistantPreferencesStore(
        IDbContextFactory<ConfigurationDbContext> dbContextFactory,
        ILogger<AssistantPreferencesStore>? logger = null)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        this.logger = logger;
    }

    public AssistantPreferences Load()
    {
        lock (sync)
        {
            try
            {
                using var db = dbContextFactory.CreateDbContext();
                var entity = db.AssistantPreferences.AsNoTracking().SingleOrDefault();
                if (entity is null) return new AssistantPreferences();

                return new AssistantPreferences
                {
                    ProviderBusinessId = NormalizeString(entity.ProviderBusinessId),
                    ModelId = NormalizeString(entity.ModelId),
                    ReasoningEffort = AssistantPreferences.NormalizeReasoningEffort(entity.ReasoningEffort),
                    PermissionMode = ParsePermissionMode(entity.PermissionMode),
                };
            }
            catch (Exception exception) when (IsRecoverable(exception))
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
            using var db = dbContextFactory.CreateDbContext();
            var entity = db.AssistantPreferences.SingleOrDefault();
            if (entity is null)
            {
                entity = new AssistantPreferencesEntity();
                db.AssistantPreferences.Add(entity);
            }

            entity.ProviderBusinessId = NormalizeString(preferences.ProviderBusinessId);
            entity.ModelId = NormalizeString(preferences.ModelId);
            entity.ReasoningEffort = AssistantPreferences.NormalizeReasoningEffort(preferences.ReasoningEffort);
            entity.PermissionMode = AssistantPreferences.NormalizePermissionMode(preferences.PermissionMode);
            db.SaveChanges();
        }
    }

    /// <summary>更新单项并落盘，返回更新后的偏好。</summary>
    public AssistantPreferences Update(Func<AssistantPreferences, AssistantPreferences> mutate)
    {
        var updated = mutate(Load());
        Save(updated);
        return updated;
    }

    /// <summary>若存在旧版 JSON 偏好文件，将其一次性迁入配置库（迁移失败保留原文件不覆盖）。</summary>
    public void MigrateLegacyIfNeeded()
    {
        lock (sync)
        {
            var legacyPath = LegacyJsonPath;
            if (!File.Exists(legacyPath)) return;

            try
            {
                var payload = JsonNode.Parse(File.ReadAllText(legacyPath)) as JsonObject;
                if (payload is null) return;
                var legacy = new AssistantPreferences
                {
                    ProviderBusinessId = ReadString(payload, "provider_business_id"),
                    ModelId = ReadString(payload, "model_id"),
                    ReasoningEffort = AssistantPreferences.NormalizeReasoningEffort(ReadString(payload, "reasoning_effort")),
                    PermissionMode = Enum.TryParse<AssistantPermissionMode>(ReadString(payload, "permission_mode"), out var mode)
                        ? mode
                        : AssistantPermissionMode.AutoApprove,
                };
                Save(legacy);
                File.Delete(legacyPath);
                logger?.LogInformation("小助手偏好已从旧版 JSON 迁入配置库");
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger?.LogWarning(exception, "小助手偏好旧版 JSON 迁移失败，保留原文件");
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or JsonException or IOException or UnauthorizedAccessException;

    private static string? NormalizeString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static AssistantPermissionMode ParsePermissionMode(string? value) =>
        Enum.TryParse<AssistantPermissionMode>(value, out var mode) ? mode : AssistantPermissionMode.AutoApprove;

    private static string? ReadString(JsonObject payload, string key) =>
        payload[key]?.GetValue<string>() is { Length: > 0 } value ? value : null;
}
