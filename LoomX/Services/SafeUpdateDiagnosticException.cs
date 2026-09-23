using System.Runtime.ExceptionServices;

namespace LoomX.Services;

/// <summary>仅携带白名单字段和安全堆栈的更新诊断异常。</summary>
internal sealed class SafeUpdateDiagnosticException : Exception
{
    private const string SafeMessage = "更新流程发生异常，原始消息已省略。";

    private SafeUpdateDiagnosticException(Exception source, string stage)
        : base(SafeMessage)
    {
        OriginalExceptionType = source.GetType().FullName ?? source.GetType().Name;
        OriginalHResult = source.HResult;
        HttpStatusCode = FindHttpStatusCode(source);
        Stage = stage;
        HResult = source.HResult;

        ExceptionDispatchInfo.SetCurrentStackTrace(this);
    }

    public string OriginalExceptionType { get; }
    public int OriginalHResult { get; }
    public int? HttpStatusCode { get; }
    public string Stage { get; }

    public static SafeUpdateDiagnosticException Create(Exception source, string stage) => new(source, stage);

    private static int? FindHttpStatusCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } statusCode })
                return (int)statusCode;
        }

        return null;
    }
}
