using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LoomX.Plugins;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>只接收安全摘要并拦截已知凭据形态；可信进程内代码仍不是防泄漏沙箱。</summary>
internal static partial class PluginFeedbackPolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "PluginId", "OriginalFormat", "SourceContext", "MessageTemplate", "EventId", "EventName", "TraceId", "SpanId", "Exception", "Level", "Timestamp" };

    public static string Prefix(string pluginId)
    {
        if (string.IsNullOrEmpty(pluginId) || pluginId.Length > 128 || !Identity().IsMatch(pluginId)) throw Invalid();
        return $"[{pluginId}] ";
    }
    public static void ValidateText(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength ||
            text.Any(character => char.IsControl(character) && character is not '\t') || SensitiveValue().IsMatch(text)) throw Invalid();
    }
    public static void ValidateFieldName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 || !FieldName().IsMatch(name) ||
            SensitiveName().IsMatch(name) || Reserved.Contains(name)) throw Invalid();
    }
    public static PluginBridgeException Invalid() => new(PluginBridgeError.InvalidArgument, "插件反馈不是允许的安全摘要。");

    /// <summary>v1 仅允许命名占位和双花括号字面量，不接受格式参数或对齐，展开总长有界。</summary>
    public static string Render(string template, IReadOnlyList<KeyValuePair<string, object?>> fields, int budget)
    {
        var output = new StringBuilder();
        void Append(string value)
        {
            if (value.Length > budget - output.Length) throw Invalid();
            output.Append(value);
        }
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character is '{' or '}')
            {
                if (index + 1 < template.Length && template[index + 1] == character)
                { Append(character.ToString()); index++; continue; }
                if (character == '}') throw Invalid();
                var close = template.IndexOf('}', index + 1);
                if (close < 0) throw Invalid();
                var name = template[(index + 1)..close];
                if (!FieldName().IsMatch(name)) throw Invalid();
                var field = fields.FirstOrDefault(item => item.Key == name);
                if (field.Key is null) throw Invalid();
                Append(Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "(null)");
                index = close;
            }
            else Append(character.ToString());
        }
        var rendered = output.ToString();
        ValidateText(rendered, budget);
        return rendered;
    }

    // Basic/Bearer 的非空凭据均须拒绝，不能因 token 较短而进入输出或拒绝诊断。
    [GeneratedRegex(@"(?i)(authorization|api[ _-]?key|password|secret|credential|header|request[ _-]?body|response[ _-]?body|prompt|tool[ _-]?(arguments|parameters)|(?:bearer|basic)\s+[a-z0-9+/_=-]+|sk-[a-z0-9_-]{8,}|gh[pousr]_[a-z0-9_]{12,}|github_pat_[a-z0-9_]{12,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----)", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValue();
    [GeneratedRegex(@"(?i)(authorization|api.?key|password|secret|credential|header|body|prompt|tool|token|payload)", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveName();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldName();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identity();
}

/// <summary>按实例限制一分钟内事件数量；无后台定时器。</summary>
internal sealed class PluginFeedbackRateLimit(int maximum)
{
    private readonly object gate = new();
    private readonly Queue<long> events = new();
    public void Acquire()
    {
        lock (gate)
        {
            var now = Environment.TickCount64;
            while (events.TryPeek(out var oldest) && now - oldest >= 60_000) events.Dequeue();
            if (events.Count >= maximum) throw new PluginBridgeException(PluginBridgeError.ResourceLimit, "插件反馈超过频率限制。");
            events.Enqueue(now);
        }
    }
}

/// <summary>拒绝路径也限频记录安全摘要，日志管线自身故障不递归放大。</summary>
internal sealed class PluginFeedbackDiagnostics(string pluginId, ILogger logger)
{
    private readonly PluginFeedbackRateLimit rateLimit = new(10);
    public void Rejected(PluginBridgeError error)
    {
        try
        {
            rateLimit.Acquire();
            logger.LogWarning("插件反馈已拒绝 {PluginId} {Error}", pluginId, error);
        }
        catch (Exception) { /* 诊断不改变原调用的稳定错误，也不把异常原值写入另一管线。 */ }
    }
}

public sealed class PluginLogBridge(string pluginId, ILogger<PluginLogBridge> logger) : IPluginLogBridge
{
    private readonly string prefix = PluginFeedbackPolicy.Prefix(pluginId);
    private readonly PluginFeedbackRateLimit rateLimit = new(100);
    private readonly PluginFeedbackDiagnostics diagnostics = new(pluginId, logger);

    public ValueTask WriteAsync(PluginLogEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (entry is null || !Enum.IsDefined(entry.Level)) throw PluginFeedbackPolicy.Invalid();
            PluginFeedbackPolicy.ValidateText(entry.MessageTemplate, 512);
            if (entry.Fields?.Count > 16) throw PluginFeedbackPolicy.Invalid();
            var fields = new List<KeyValuePair<string, object?>> { new("PluginId", pluginId) };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in entry.Fields ?? [])
            {
                if (field is null || field.Value is null) throw PluginFeedbackPolicy.Invalid();
                PluginFeedbackPolicy.ValidateFieldName(field.Name);
                if (!names.Add(field.Name)) throw PluginFeedbackPolicy.Invalid();
                var value = field.Value;
                object? mapped = value.Kind switch
                {
                    PluginLogValueKind.Null => null,
                    PluginLogValueKind.Boolean => value.Boolean,
                    PluginLogValueKind.Integer => value.Integer,
                    PluginLogValueKind.Real when double.IsFinite(value.Real) => value.Real,
                    PluginLogValueKind.Text => ValidateValue(value.Text),
                    _ => throw PluginFeedbackPolicy.Invalid(),
                };
                fields.Add(new(field.Name, mapped));
            }
            var rendered = prefix + PluginFeedbackPolicy.Render(entry.MessageTemplate, fields, 512 - prefix.Length);
            fields.Add(new("{OriginalFormat}", prefix + entry.MessageTemplate));
            rateLimit.Acquire();
            logger.Log(entry.Level switch
            {
                PluginLogLevel.Debug => LogLevel.Debug,
                PluginLogLevel.Information => LogLevel.Information,
                PluginLogLevel.Warning => LogLevel.Warning,
                PluginLogLevel.Error => LogLevel.Error,
                _ => throw PluginFeedbackPolicy.Invalid(),
            }, default, new LogState(fields, rendered), null, static (state, _) => state.Rendered);
            return ValueTask.CompletedTask;
        }
        catch (PluginBridgeException exception) { diagnostics.Rejected(exception.Error); throw; }
        catch (Exception)
        {
            diagnostics.Rejected(PluginBridgeError.OperationFailed);
            throw new PluginBridgeException(PluginBridgeError.OperationFailed, "插件日志投递失败。");
        }
    }
    private static string ValidateValue(string? value) { PluginFeedbackPolicy.ValidateText(value, 256); return value!; }
    private sealed record LogState(List<KeyValuePair<string, object?>> Fields, string Rendered) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => Fields.Count;
        public KeyValuePair<string, object?> this[int index] => Fields[index];
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Fields.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>只能请求带宿主来源的 Toast，不暴露窗口、导航或调度控制。</summary>
public sealed class PluginNotificationBridge(string pluginId, ToastService toastService, ILogger<PluginNotificationBridge> logger) : IPluginNotificationBridge
{
    private readonly string prefix = PluginFeedbackPolicy.Prefix(pluginId);
    private readonly PluginFeedbackRateLimit rateLimit = new(10);
    private readonly PluginFeedbackDiagnostics diagnostics = new(pluginId, logger);
    public ValueTask ShowAsync(PluginNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (notification is null || !Enum.IsDefined(notification.Level)) throw PluginFeedbackPolicy.Invalid();
            PluginFeedbackPolicy.ValidateText(notification.Message, 512 - prefix.Length);
            rateLimit.Acquire();
            var level = notification.Level switch
            {
                PluginNotificationLevel.Success => ToastLevel.Success,
                PluginNotificationLevel.Info => ToastLevel.Info,
                PluginNotificationLevel.Warning => ToastLevel.Warning,
                PluginNotificationLevel.Error => ToastLevel.Error,
                _ => throw PluginFeedbackPolicy.Invalid(),
            };
            if (!toastService.TryShowIsolated(prefix + notification.Message, level))
                throw new PluginBridgeException(PluginBridgeError.OperationFailed, "插件通知投递部分失败，故障订阅已隔离。");
            logger.LogInformation("插件通知完成 {PluginId} {Level}", pluginId, notification.Level);
            return ValueTask.CompletedTask;
        }
        catch (PluginBridgeException exception) { diagnostics.Rejected(exception.Error); throw; }
        catch (Exception)
        {
            diagnostics.Rejected(PluginBridgeError.OperationFailed);
            throw new PluginBridgeException(PluginBridgeError.OperationFailed, "插件通知投递失败。");
        }
    }
}