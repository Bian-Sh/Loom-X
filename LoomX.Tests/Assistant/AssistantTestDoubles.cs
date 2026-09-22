using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LoomX.Tests.Assistant;

/// <summary>测试用 HTTP 处理器：同步返回预置响应，不发真实网络请求。</summary>
internal sealed class DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

/// <summary>测试用 DbContext 工厂：每个测试用独立临时 SQLite 文件。</summary>
internal sealed class TestDbContextFactory(DbContextOptions<ConfigurationDbContext> options) : IDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext() => new(options);

    public Task<ConfigurationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationDbContext(options));
}
