namespace LoomX.CredentialProtection;

/// <summary>同步 JSON/SSE 解析链唯一等待点；工作线程启动异步操作，避免捕获 UI 同步上下文。</summary>
internal static class CredentialSynchronous
{
    internal static T Run<T>(Func<ValueTask<T>> operation) =>
        Task.Run(async () => await operation().ConfigureAwait(false)).GetAwaiter().GetResult();
}
