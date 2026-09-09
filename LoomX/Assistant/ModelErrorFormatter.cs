using System.Globalization;
using System.Text;
using LoomX.Localization;

namespace LoomX.Assistant;

/// <summary>
/// 小助手模型错误的多语言详情组装：
/// 第一行为错误类别的本地化描述（assistant.error.kind.*），
/// 后续按需追加 HTTP 状态码、Provider 错误码、脱敏后的上游描述，供聊天页完整展示。
/// </summary>
public static class ModelErrorFormatter
{
    /// <summary>按当前 UI 文化组装错误详情（多行文本）。</summary>
    public static string Format(
        ModelErrorKind kind,
        int? statusCode = null,
        string? errorCode = null,
        string? upstreamMessage = null)
    {
        var culture = LocaleService.CurrentCulture;
        var builder = new StringBuilder(ResourceLookup.Resolve(KindResourceKey(kind), culture));

        if (statusCode is int status)
        {
            builder.AppendLine().Append(FormatValue("assistant.error.status", culture, status));
        }
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            builder.AppendLine().Append(FormatValue("assistant.error.code", culture, errorCode));
        }
        if (!string.IsNullOrWhiteSpace(upstreamMessage))
        {
            builder.AppendLine().Append(FormatValue("assistant.error.upstream", culture, upstreamMessage));
        }

        return builder.ToString();
    }

    /// <summary>把任意异常转换为错误详情；ModelClientException 直接消费其结构化字段。</summary>
    public static string FormatException(Exception exception) => exception switch
    {
        ModelClientException modelError => Format(
            modelError.Kind,
            modelError.StatusCode,
            modelError.ErrorCode,
            modelError.UpstreamMessage),
        _ => Format(
            ModelErrorKind.Unknown,
            upstreamMessage: ModelErrorClassifier.SanitizeUpstreamMessage(exception.Message)),
    };

    /// <summary>超过最大步骤数的失败详情。</summary>
    public static string FormatMaxStepsExceeded(int maxSteps) =>
        FormatValue("assistant.error.max_steps", LocaleService.CurrentCulture, maxSteps);

    /// <summary>错误类别 → resx 键。</summary>
    public static string KindResourceKey(ModelErrorKind kind) => kind switch
    {
        ModelErrorKind.ConnectionFailed => "assistant.error.kind.connection_failed",
        ModelErrorKind.Timeout => "assistant.error.kind.timeout",
        ModelErrorKind.Authentication => "assistant.error.kind.authentication",
        ModelErrorKind.PermissionDenied => "assistant.error.kind.permission_denied",
        ModelErrorKind.ModelNotFound => "assistant.error.kind.model_not_found",
        ModelErrorKind.InvalidRequest => "assistant.error.kind.invalid_request",
        ModelErrorKind.ContextLengthExceeded => "assistant.error.kind.context_length",
        ModelErrorKind.RateLimited => "assistant.error.kind.rate_limited",
        ModelErrorKind.InsufficientQuota => "assistant.error.kind.insufficient_quota",
        ModelErrorKind.ContentFiltered => "assistant.error.kind.content_filtered",
        ModelErrorKind.ServerOverloaded => "assistant.error.kind.overloaded",
        ModelErrorKind.ServerError => "assistant.error.kind.server_error",
        _ => "assistant.error.kind.unknown",
    };

    private static string FormatValue(string key, CultureInfo culture, object argument) =>
        string.Format(culture, ResourceLookup.Resolve(key, culture), argument);
}
