using Microsoft.EntityFrameworkCore;
using LoomX.Configuration;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class SettingsViewModelTabLifecycleTests
{
    [Fact]
    public async Task 更新Tab首次进入加载历史且重复进入不重复请求并保持注入实例所有权()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "LoomX.db");
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        await using (var db = new ConfigurationDbContext(options)) await ConfigurationDatabase.InitializeAsync(db);

        using var configService = new ConfigSnapshotService(databasePath);
        using var gatewayService = new GatewayProcessService();
        using var dataStore = new AppDataStore(configService, gatewayService);
        await dataStore.InitializeAsync();
        var service = new CountingUpdateService();
        using var coordinator = new UpdateCoordinator(dataStore, service, dispatch: action => action());
        using var history = new ReleaseHistoryViewModel(service, _ => Task.FromResult(new UpdateProxySettings(false, "direct", string.Empty, 0, null, null)), dispatch: action => action());
        var constructor = Assert.Single(typeof(SettingsViewModel).GetConstructors(), item =>
            item.GetParameters().Any(parameter => parameter.ParameterType == typeof(ReleaseHistoryViewModel)));
        var arguments = constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(AppDataStore) ? (object?)dataStore :
            parameter.ParameterType == typeof(UpdateCoordinator) ? coordinator :
            parameter.ParameterType == typeof(ReleaseHistoryViewModel) ? history :
            null).ToArray();
        var settings = Assert.IsType<SettingsViewModel>(constructor.Invoke(arguments));
        var selectedTabIndex = typeof(SettingsViewModel).GetProperty("SelectedTabIndex");
        var releaseHistory = typeof(SettingsViewModel).GetProperty("ReleaseHistory");
        Assert.NotNull(selectedTabIndex);
        Assert.NotNull(releaseHistory);
        await WaitForAsync(() => !settings.IsBusy);

        selectedTabIndex.SetValue(settings, 1);
        await Task.Delay(50);
        Assert.Equal(0, service.HistoryRequests);

        selectedTabIndex.SetValue(settings, 2);
        await WaitForAsync(() => service.HistoryRequests == 1 && !history.IsInitialLoading);
        selectedTabIndex.SetValue(settings, 0);
        selectedTabIndex.SetValue(settings, 2);
        await Task.Delay(50);

        Assert.Same(history, releaseHistory.GetValue(settings));
        Assert.Equal(1, service.HistoryRequests);

        settings.Dispose();
        history.RefreshCommand.Execute(null);
        await WaitForAsync(() => service.HistoryRequests == 2 && !history.IsRefreshing);
        Assert.Equal(2, service.HistoryRequests);

        try { Directory.Delete(directory, true); } catch { }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("等待设置页状态超时。");
            await Task.Delay(10);
        }
    }

    private sealed class CountingUpdateService : IUpdateService
    {
        private int historyRequests;
        public int HistoryRequests => Volatile.Read(ref historyRequests);

        public Task<UpdateReleasePage> GetStableReleasesAsync(UpdateProxySettings settings, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref historyRequests);
            return Task.FromResult(new UpdateReleasePage([], page, pageSize, false));
        }

        public Task<UpdateCheckResult> CheckAsync(UpdateProxySettings settings, CancellationToken cancellationToken = default) => Task.FromResult(new UpdateCheckResult(AppVersion.Current, null));
        public Task<PreparedUpdate> PrepareUpdateAsync(UpdateRelease release, UpdateProxySettings settings, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void LaunchInstaller(PreparedUpdate preparedUpdate) => throw new NotSupportedException();
    }
}
