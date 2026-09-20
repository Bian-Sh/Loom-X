using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class ReleaseHistoryViewModelTests
{
    private static readonly UpdateProxySettings DirectSettings = new(false, "direct", string.Empty, 0, null, null);

    [Fact]
    public async Task 首次加载固定请求十条并默认选择最高正式版本()
    {
        var service = FakeUpdateService.WithPages(
            Page(1, false, Release("0.12.8", "较早"), Release("0.13.0", "最新"), Release("0.12.9", "中间")));
        using var vm = CreateHistory(service);

        await vm.EnsureLoadedAsync();

        Assert.Equal([(1, 10)], service.Requests);
        Assert.Equal("0.13.0", vm.SelectedRelease?.Release.Version);
        Assert.Equal("最新", vm.Content.Markdown.ToString());
        Assert.False(vm.IsInitialLoading);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task 最新版本徽标和当前版本徽标彼此独立()
    {
        var service = FakeUpdateService.WithPages(
            Page(1, false, Release("0.13.0"), Release(AppVersion.Current)));
        using var vm = CreateHistory(service);

        await vm.EnsureLoadedAsync();

        var latest = vm.Releases.Single(item => item.Release.Version == "0.13.0");
        var current = vm.Releases.Single(item => item.Release.Version == AppVersion.Current);
        Assert.True(latest.IsLatest);
        Assert.False(latest.IsCurrent);
        Assert.True(current.IsCurrent);
        Assert.False(current.IsLatest);
    }

    [Fact]
    public async Task 切换版本只替换共享正文且不增加网络调用()
    {
        var service = FakeUpdateService.WithPages(
            Page(1, false, Release("0.13.0", "新正文"), Release("0.12.9", "旧正文")));
        using var vm = CreateHistory(service);
        await vm.EnsureLoadedAsync();
        var content = vm.Content;
        var previousBuilder = content.Markdown;

        vm.SelectedRelease = vm.Releases.Single(item => item.Release.Version == "0.12.9");

        Assert.Same(content, vm.Content);
        Assert.NotSame(previousBuilder, vm.Content.Markdown);
        Assert.Equal("旧正文", vm.Content.Markdown.ToString());
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task 加载更多按标准化版本去重并保留选择和正文()
    {
        var service = FakeUpdateService.WithPages(
            Page(1, true, Release("0.13.0", "最新正文"), Release("0.12.9", "选中正文")),
            Page(2, false, Release("v0.12.9", "重复正文"), Release("0.12.8", "更早正文")));
        using var vm = CreateHistory(service);
        await vm.EnsureLoadedAsync();
        vm.SelectedRelease = vm.Releases.Single(item => item.Release.Version == "0.12.9");
        var selected = vm.SelectedRelease;
        var contentBuilder = vm.Content.Markdown;

        vm.LoadMoreCommand.Execute(null);
        await service.WaitForRequestCountAsync(2);
        await WaitForAsync(() => !vm.IsLoadingMore);

        Assert.Equal(new[] { "0.13.0", "0.12.9", "0.12.8" }, vm.Releases.Select(item => item.Release.Version));
        Assert.Same(selected, vm.SelectedRelease);
        Assert.Same(contentBuilder, vm.Content.Markdown);
        Assert.Equal("选中正文", vm.Content.Markdown.ToString());
        Assert.Equal([(1, 10), (2, 10)], service.Requests);
        Assert.False(vm.HasMore);
    }

    [Fact]
    public async Task 刷新失败保留集合选择和正文并标记缓存内容()
    {
        const string secret = "不应出现在用户文案中的上游正文";
        var service = FakeUpdateService.WithPages(
            Page(1, false, Release("0.13.0", "缓存正文"), Release("0.12.9", "选中正文")));
        using var vm = CreateHistory(service);
        await vm.EnsureLoadedAsync();
        vm.SelectedRelease = vm.Releases.Single(item => item.Release.Version == "0.12.9");
        var collection = vm.Releases;
        var selected = vm.SelectedRelease;
        var builder = vm.Content.Markdown;
        service.EnqueueFailure(new InvalidOperationException(secret));

        vm.RefreshCommand.Execute(null);
        await service.WaitForRequestCountAsync(2);
        await WaitForAsync(() => !vm.IsRefreshing);

        Assert.Same(collection, vm.Releases);
        Assert.Same(selected, vm.SelectedRelease);
        Assert.Same(builder, vm.Content.Markdown);
        Assert.Equal("选中正文", vm.Content.Markdown.ToString());
        Assert.True(vm.HasError);
        Assert.True(vm.HasCachedContent);
        Assert.DoesNotContain(secret, vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 刷新成功优先恢复原版本并一次替换缓存集合()
    {
        var service = FakeUpdateService.WithPages(
            Page(1, false, Release("0.13.0", "旧最新"), Release("0.12.9", "旧选择")));
        using var vm = CreateHistory(service);
        await vm.EnsureLoadedAsync();
        vm.SelectedRelease = vm.Releases.Single(item => item.Release.Version == "0.12.9");
        var previousCollection = vm.Releases;
        service.EnqueuePage(Page(1, false, Release("0.13.1", "新最新"), Release("0.12.9", "刷新选择")));

        vm.RefreshCommand.Execute(null);
        await service.WaitForRequestCountAsync(2);
        await WaitForAsync(() => !vm.IsRefreshing);

        Assert.NotSame(previousCollection, vm.Releases);
        Assert.Equal("0.12.9", vm.SelectedRelease?.Release.Version);
        Assert.Equal("刷新选择", vm.Content.Markdown.ToString());
        Assert.False(vm.HasError);
        Assert.False(vm.HasCachedContent);
    }

    [Fact]
    public async Task 首次空响应进入空态并清空选择()
    {
        var service = FakeUpdateService.WithPages(Page(1, false));
        using var vm = CreateHistory(service);

        await vm.EnsureLoadedAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasError);
        Assert.Null(vm.SelectedRelease);
        Assert.True(vm.Content.IsEmpty);
    }

    [Fact]
    public async Task 首次失败进入错误态且可通过加载命令重试()
    {
        const string secret = "私密异常详情";
        var service = FakeUpdateService.WithFailure(new InvalidOperationException(secret));
        using var vm = CreateHistory(service);

        await vm.EnsureLoadedAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.HasCachedContent);
        Assert.False(vm.IsEmpty);
        Assert.DoesNotContain(secret, vm.ErrorText, StringComparison.Ordinal);
        Assert.True(vm.LoadCommand.CanExecute(null));

        service.EnqueuePage(Page(1, false, Release("0.13.0", "重试成功")));
        vm.LoadCommand.Execute(null);
        await service.WaitForRequestCountAsync(2);
        await WaitForAsync(() => !vm.IsInitialLoading);

        Assert.False(vm.HasError);
        Assert.Equal("0.13.0", vm.SelectedRelease?.Release.Version);
    }

    [Fact]
    public async Task 文化切换只刷新日期和用户文案而不请求网络()
    {
        var originalCulture = LocaleService.CurrentCulture.Name;
        try
        {
            LocaleService.SetCulture("en-US");
            var service = FakeUpdateService.WithPages(
                Page(1, false, Release("0.13.0", publishedAt: new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero))));
            using var vm = CreateHistory(service);
            await vm.EnsureLoadedAsync();
            var englishDate = vm.Releases[0].PublishedAtText;

            LocaleService.SetCulture("zh-CN");

            Assert.NotEqual(englishDate, vm.Releases[0].PublishedAtText);
            Assert.Single(service.Requests);
        }
        finally
        {
            LocaleService.SetCulture(originalCulture);
        }
    }

    [Fact]
    public async Task 缺失资源时错误文案使用安全回退且所有状态变更经由注入调度()
    {
        var dispatchCalls = 0;
        var service = FakeUpdateService.WithFailure(new InvalidOperationException("不得显示"));
        using var vm = CreateHistory(
            service,
            localizer: new MissingLocalizer(),
            dispatch: action =>
            {
                dispatchCalls++;
                action();
            });

        await vm.EnsureLoadedAsync();

        Assert.Equal("无法加载版本历史，请重试。", vm.ErrorText);
        Assert.True(dispatchCalls >= 2);
    }

    private static ReleaseHistoryViewModel CreateHistory(
        FakeUpdateService service,
        IStringLocalizer<ReleaseHistoryViewModel>? localizer = null,
        Action<Action>? dispatch = null) =>
        new(
            service,
            _ => Task.FromResult(DirectSettings),
            logger: new TestLogger<ReleaseHistoryViewModel>(),
            localizer: localizer,
            dispatch: dispatch ?? (action => action()));

    private static UpdateReleasePage Page(int page, bool hasMore, params UpdateRelease[] releases) =>
        new(releases, page, 10, hasMore);

    private static UpdateRelease Release(
        string version,
        string body = "正文",
        DateTimeOffset? publishedAt = null) =>
        new(
            $"v{version.TrimStart('v', 'V')}",
            version,
            $"Loom-X {version}",
            body,
            $"https://github.com/Bian-Sh/Loom-X/releases/tag/v{version.TrimStart('v', 'V')}",
            publishedAt ?? new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero),
            [],
            null,
            null);

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("等待 ViewModel 状态超时。");
            await Task.Delay(10);
        }
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly ConcurrentQueue<object> responses = new();
        private readonly object sync = new();
        private TaskCompletionSource requestChanged = NewSignal();

        public List<(int Page, int PageSize)> Requests { get; } = [];

        public static FakeUpdateService WithPages(params UpdateReleasePage[] pages)
        {
            var service = new FakeUpdateService();
            foreach (var page in pages) service.EnqueuePage(page);
            return service;
        }

        public static FakeUpdateService WithFailure(Exception exception)
        {
            var service = new FakeUpdateService();
            service.EnqueueFailure(exception);
            return service;
        }

        public void EnqueuePage(UpdateReleasePage page) => responses.Enqueue(page);
        public void EnqueueFailure(Exception exception) => responses.Enqueue(exception);

        public async Task WaitForRequestCountAsync(int expected)
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (true)
            {
                Task signal;
                lock (sync)
                {
                    if (Requests.Count >= expected) return;
                    signal = requestChanged.Task;
                }

                var remaining = timeout - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("等待 Release 请求超时。");
                await signal.WaitAsync(remaining);
            }
        }

        public Task<UpdateReleasePage> GetStableReleasesAsync(
            UpdateProxySettings settings,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                Requests.Add((page, pageSize));
                requestChanged.TrySetResult();
                requestChanged = NewSignal();
            }

            if (!responses.TryDequeue(out var response))
                throw new InvalidOperationException("测试未配置 Release 响应。");
            if (response is Exception exception) return Task.FromException<UpdateReleasePage>(exception);
            return Task.FromResult((UpdateReleasePage)response);
        }

        public Task<UpdateCheckResult> CheckAsync(UpdateProxySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PreparedUpdate> PrepareUpdateAsync(
            UpdateRelease release,
            UpdateProxySettings settings,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void LaunchInstaller(PreparedUpdate preparedUpdate) => throw new NotSupportedException();

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class MissingLocalizer : IStringLocalizer<ReleaseHistoryViewModel>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
