using System.Collections.Concurrent;
using LoomX.Configuration;
using LoomX.Localization;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ProvidersViewModelTestPanelTests
{
    [Fact]
    public void 构造时创建测试面板并绑定当前提供商模型()
    {
        using var fixture = TestFixture.Create();
        using var viewModel = fixture.CreateViewModel();
        var provider = CreateProvider("provider-one");
        var model = AddModel(provider, "model-one");

        viewModel.Providers.Add(provider);
        viewModel.SelectedProvider = provider;

        Assert.NotNull(viewModel.TestPanel);
        Assert.Same(model, viewModel.TestPanel.SelectedModel);
    }

    [Fact]
    public async Task 切换提供商会取消旧测试请求并绑定新模型()
    {
        using var fixture = TestFixture.Create();
        var service = new RecordingProviderTestService(blockUntilCancelled: true);
        using var viewModel = fixture.CreateViewModel(service);
        var first = CreateProvider("provider-one");
        AddModel(first, "model-one");
        var second = CreateProvider("provider-two");
        var secondModel = AddModel(second, "model-two");
        viewModel.Providers.Add(first);
        viewModel.Providers.Add(second);
        viewModel.SelectedProvider = first;

        viewModel.TestPanel.SendCommand.Execute(null);
        await service.WaitForStartedAsync();
        viewModel.SelectedProvider = second;

        await service.WaitForCancellationAsync();
        Assert.Same(secondModel, viewModel.TestPanel.SelectedModel);
        Assert.False(viewModel.TestPanel.HasResult);
    }

    [Fact]
    public async Task 测试请求读取未保存的提供商内存快照()
    {
        using var fixture = TestFixture.Create();
        var service = new RecordingProviderTestService();
        using var viewModel = fixture.CreateViewModel(service);
        var provider = CreateProvider("provider-memory", "https://memory.example/v1");
        provider.ApiKey = "memory-api-key";
        provider.ApiMode = "anthropic";
        provider.EndpointFormat = "chat_completions";
        provider.UseProxy = true;
        provider.AddHeader();
        provider.Headers[0].Name = "X-Memory-Header";
        provider.Headers[0].Value = "memory-value";
        var model = AddModel(provider, "memory-model");
        viewModel.Providers.Add(provider);
        viewModel.SelectedProvider = provider;
        viewModel.TestPanel.Prompt = "未保存提示词";
        viewModel.TestPanel.SelectedMode = ProviderTestMode.Streaming;
        viewModel.TestPanel.SelectedModel = model;

        viewModel.TestPanel.SendCommand.Execute(null);
        var request = await service.WaitForStartedAsync();

        Assert.Equal("provider-memory", request.ProviderId);
        Assert.Equal("memory-model", request.ModelId);
        Assert.Equal("https://memory.example/v1", request.BaseUrl);
        Assert.Equal("anthropic", request.ApiMode);
        Assert.Equal("chat_completions", request.EndpointFormat);
        Assert.Equal("memory-api-key", request.ApiKey);
        Assert.Equal("memory-value", request.Headers["X-Memory-Header"]);
        Assert.True(request.UseProxy);
        Assert.Equal("未保存提示词", request.Prompt);
        Assert.Equal(ProviderTestMode.Streaming, request.Mode);
    }

    [Fact]
    public async Task Dispose会取消正在执行的测试请求()
    {
        using var fixture = TestFixture.Create();
        var service = new RecordingProviderTestService(blockUntilCancelled: true);
        var viewModel = fixture.CreateViewModel(service);
        var provider = CreateProvider("provider-dispose");
        AddModel(provider, "model-dispose");
        viewModel.Providers.Add(provider);
        viewModel.SelectedProvider = provider;

        viewModel.TestPanel.SendCommand.Execute(null);
        await service.WaitForStartedAsync();
        viewModel.Dispose();

        await service.WaitForCancellationAsync();
    }

    [Fact]
    public void 文化变化会转发给测试面板()
    {
        using var fixture = TestFixture.Create();
        using var viewModel = fixture.CreateViewModel();
        var notified = false;
        viewModel.TestPanel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.TestPanel.Prompt)) notified = true;
        };
        var previousCulture = LocaleService.CurrentCulture.Name;
        var nextCulture = string.Equals(previousCulture, "en-US", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        try
        {
            LocaleService.SetCulture(nextCulture);
            Assert.True(notified);
        }
        finally
        {
            LocaleService.SetCulture(previousCulture);
        }
    }

    private static ProviderEditorViewModel CreateProvider(string businessId, string baseUrl = "https://example.com/v1")
    {
        var id = Guid.NewGuid();
        return ProviderEditorViewModel.FromResponse(new ProviderResponse(
            id,
            businessId,
            businessId,
            baseUrl,
            "openai",
            true,
            false,
            true,
            0,
            "{}",
            [],
            null,
            "responses",
            "configured-key"));
    }

    private static ModelEditorViewModel AddModel(ProviderEditorViewModel provider, string modelId)
    {
        var model = new ModelEditorViewModel
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.BusinessId,
            ModelId = modelId,
            DisplayName = modelId,
            Enabled = true,
            ApiMode = provider.ApiMode,
        };
        provider.Models.Add(model);
        return model;
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly string directory;
        private readonly ConfigSnapshotService configService;
        private readonly GatewayProcessService gatewayService;
        private readonly AppDataStore dataStore;

        private TestFixture(string directory, ConfigSnapshotService configService, GatewayProcessService gatewayService, AppDataStore dataStore)
        {
            this.directory = directory;
            this.configService = configService;
            this.gatewayService = gatewayService;
            this.dataStore = dataStore;
        }

        public static TestFixture Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "LoomXTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var configService = new ConfigSnapshotService(Path.Combine(directory, "LoomX.db"));
            var gatewayService = new GatewayProcessService();
            var dataStore = new AppDataStore(configService, gatewayService);
            return new TestFixture(directory, configService, gatewayService, dataStore);
        }

        public ProvidersViewModel CreateViewModel(IProviderTestService? service = null) =>
            new(dataStore, providerTestService: service);

        public void Dispose()
        {
            dataStore.Dispose();
            gatewayService.Dispose();
            configService.Dispose();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private sealed class RecordingProviderTestService : IProviderTestService
    {
        private readonly bool blockUntilCancelled;
        private readonly TaskCompletionSource<ProviderTestRequest> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<ProviderTestRequest> Requests { get; } = new();

        public RecordingProviderTestService(bool blockUntilCancelled = false) => this.blockUntilCancelled = blockUntilCancelled;

        public Task<ProviderTestRequest> WaitForStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task WaitForCancellationAsync() => cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<ProviderTestResult> ExecuteAsync(ProviderTestRequest request, IProgress<ProviderTestProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            started.TrySetResult(request);
            if (blockUntilCancelled)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled.TrySetResult(true);
                    return CreateResult(request, ProviderTestStatus.Cancelled);
                }
            }

            return CreateResult(request, ProviderTestStatus.Completed);
        }

        private static ProviderTestResult CreateResult(ProviderTestRequest request, ProviderTestStatus status) =>
            new(request.RequestId, status, new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "test", "/test", request.Mode, request.UseProxy, "direct", null, request.Headers.Count), 200, "application/json", 1, 0, status == ProviderTestStatus.Completed ? "测试响应" : "");
    }
}
