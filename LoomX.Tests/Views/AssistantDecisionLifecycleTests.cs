using Avalonia.Controls;
using LoomX.Activity;
using LoomX.Assistant.UserDecisions;
using LoomX.Configuration;
using LoomX.Services;
using LoomX.ViewModels;
using LoomX.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AssistantDecisionLifecycleTests
{
    [Fact]
    public async Task AssistantView_挂载不订阅且活动请求在卸载时取消已Claim请求()
    {
        AvaloniaTestBootstrap.Ensure();
        using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
        var dialogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new AssistantViewModel(
            new GatewayProcessService(),
            userDecisionBroker: broker,
            uiDispatcher: action => action(),
            showAskUserDialog: _ =>
            {
                dialogStarted.TrySetResult();
                return dialogCompletion.Task;
            });
        var view = new AssistantView { DataContext = viewModel };

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.RequestAsync(
            "before-attach",
            CreateRequest(),
            CancellationToken.None));

        var host = new Window { Content = view, ShowActivated = false };
        host.Show();

        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.RequestAsync(
            "after-attach",
            CreateRequest(),
            CancellationToken.None));

        viewModel.Activate(); // 模拟 SendAsync 已进入真实请求边界。
        var task = broker.RequestAsync("active-request", CreateRequest(), CancellationToken.None);
        await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.Close();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Cancelled);
        dialogCompletion.TrySetResult(true);
    }

    [Fact]
    public async Task MainWindowViewModel_Dispose幂等释放Assistant并收敛已Claim请求()
    {
        AvaloniaTestBootstrap.Ensure();
        var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "LoomX.db");
        var activityPath = Path.Combine(directory, "LoomX.Activity.db");
        try
        {
            await InitializeConfigurationAsync(configPath);
            using var configService = new ConfigSnapshotService(configPath);
            using var gatewayService = new GatewayProcessService();
            using var store = new AppDataStore(
                configService,
                gatewayService,
                NullLogger<AppDataStore>.Instance,
                new ActivityQueryService(activityPath));
            await store.InitializeAsync();
            using var broker = new UserDecisionBroker(NullLogger<UserDecisionBroker>.Instance);
            var dialogStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialogCompletion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var assistant = new AssistantViewModel(
                gatewayService,
                userDecisionBroker: broker,
                uiDispatcher: action => action(),
                showAskUserDialog: _ =>
                {
                    dialogStarted.TrySetResult();
                    return dialogCompletion.Task;
                });
            assistant.Activate();
            var main = new MainWindowViewModel(
                gatewayService,
                configService: configService,
                dataStore: store,
                assistantViewModel: assistant);
            var task = broker.RequestAsync("main-window", CreateRequest(), CancellationToken.None);
            await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            main.Dispose();
            main.Dispose();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(result.Cancelled);
            dialogCompletion.TrySetResult(true);
            store.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(directory);
        }
    }

    private static UserDecisionRequest CreateRequest() => new(
        "确认",
        "请选择",
        [new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true)]);

    private static async Task InitializeConfigurationAsync(string path)
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        await using var db = new ConfigurationDbContext(options);
        await ConfigurationDatabase.InitializeAsync(db);
    }

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }
}
