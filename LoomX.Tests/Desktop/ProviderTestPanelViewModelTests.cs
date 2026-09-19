using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ProviderTestPanelViewModelTests
{
    [Fact]
    public void 默认状态使用每日一言和常规模式()
    {
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());
        Assert.Equal("每日一言", panel.Prompt);
        Assert.Equal(ProviderTestMode.Regular, panel.SelectedMode);
        Assert.False(panel.CanSend);
    }

    [Fact]
    public void 绑定Provider后选择第一个启用真实模型()
    {
        var provider = new ProviderEditorViewModel { BusinessId = "p", BaseUrl = "https://example.com", ApiMode = "openai", EndpointFormat = "responses" };
        provider.Models.Add(new ModelEditorViewModel { ModelId = "disabled", Enabled = false });
        provider.Models.Add(new ModelEditorViewModel { ModelId = "enabled", Enabled = true });
        provider.Models.Add(ModelEditorViewModel.CreatePlaceholder());
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());

        panel.BindProvider(provider);

        Assert.Collection(panel.TestableModels, model => Assert.Equal("enabled", model.ModelId));
        Assert.Equal("enabled", panel.SelectedModel?.ModelId);
        Assert.True(panel.HasTestableModels);
        Assert.True(panel.CanSend);
    }

    [Fact]
    public void 模型启用状态变化会实时更新测试列表和选择()
    {
        var provider = new ProviderEditorViewModel { BusinessId = "p", BaseUrl = "https://example.com", ApiMode = "openai", EndpointFormat = "responses" };
        var first = new ModelEditorViewModel { ModelId = "first", Enabled = true };
        var second = new ModelEditorViewModel { ModelId = "second", Enabled = true };
        provider.Models.Add(first);
        provider.Models.Add(second);
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());
        panel.BindProvider(provider);
        panel.SelectedModel = second;

        second.Enabled = false;

        Assert.Collection(panel.TestableModels, model => Assert.Same(first, model));
        Assert.Same(first, panel.SelectedModel);
        Assert.True(panel.CanSend);

        first.Enabled = false;

        Assert.Empty(panel.TestableModels);
        Assert.Null(panel.SelectedModel);
        Assert.False(panel.HasTestableModels);
        Assert.False(panel.CanSend);

        second.Enabled = true;

        Assert.Collection(panel.TestableModels, model => Assert.Same(second, model));
        Assert.Same(second, panel.SelectedModel);
        Assert.True(panel.HasTestableModels);
        Assert.True(panel.CanSend);
    }

    [Fact]
    public void 模型集合变化会实时更新测试列表()
    {
        var provider = new ProviderEditorViewModel { BusinessId = "p", BaseUrl = "https://example.com", ApiMode = "openai", EndpointFormat = "responses" };
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());
        panel.BindProvider(provider);
        var model = new ModelEditorViewModel { ModelId = "added", Enabled = true };

        provider.Models.Add(model);

        Assert.Collection(panel.TestableModels, item => Assert.Same(model, item));
        Assert.Same(model, panel.SelectedModel);

        provider.Models.Remove(model);

        Assert.Empty(panel.TestableModels);
        Assert.Null(panel.SelectedModel);
        Assert.False(panel.CanSend);
    }

    [Fact]
    public void 绑定空BaseUrl的Provider时请求摘要安全降级()
    {
        var provider = new ProviderEditorViewModel
        {
            BusinessId = "provider-new",
            BaseUrl = "",
            ApiMode = "openai",
            EndpointFormat = "responses",
        };
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());

        panel.BindProvider(provider);

        Assert.Equal("POST · direct · 0 Headers", panel.RequestSummary);
    }

    [Fact]
    public void 绑定Provider后立即生成请求摘要并随隐藏配置实时更新()
    {
        var provider = new ProviderEditorViewModel
        {
            BusinessId = "provider-hidden",
            BaseUrl = "https://example.com/v1",
            ApiMode = "openai",
            EndpointFormat = "responses",
            UseProxy = false,
        };
        var firstModel = new ModelEditorViewModel { ModelId = "model-one", Enabled = true };
        var secondModel = new ModelEditorViewModel { ModelId = "model-two", Enabled = true };
        provider.Models.Add(firstModel);
        provider.Models.Add(secondModel);
        provider.AddHeader();
        provider.Headers[0].Name = "X-Test";
        provider.Headers[0].Value = "value";
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());

        panel.BindProvider(provider);

        Assert.Contains("POST https://example.com/v1/responses", panel.RequestSummary, StringComparison.Ordinal);
        Assert.Contains("direct", panel.RequestSummary, StringComparison.Ordinal);
        Assert.Contains("1 Header", panel.RequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-hidden", panel.RequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("model-one", panel.RequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Regular", panel.RequestSummary, StringComparison.Ordinal);

        panel.SelectedModel = secondModel;
        panel.SelectedMode = ProviderTestMode.Streaming;
        Assert.DoesNotContain("model-two", panel.RequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Streaming", panel.RequestSummary, StringComparison.Ordinal);

        provider.BaseUrl = "https://changed.example/api";
        provider.EndpointFormat = "chat_completions";
        provider.UseProxy = true;

        Assert.Contains("POST https://changed.example/api/chat/completions", panel.RequestSummary, StringComparison.Ordinal);
        Assert.Contains("proxy", panel.RequestSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("direct", panel.RequestSummary, StringComparison.Ordinal);

        provider.ApiMode = "anthropic";
        provider.BaseUrl = "https://changed.example/v1";
        Assert.Contains("POST https://changed.example/v1/v1/messages", panel.RequestSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void 面板公开停止命令但不公开重试命令()
    {
        var properties = typeof(ProviderTestPanelViewModel).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains("StopCommand", properties);
        Assert.DoesNotContain("RetryCommand", properties);
    }

    [Fact]
    public async Task 停止命令取消当前请求并保留已收到的响应且允许再次发送()
    {
        var service = new CancellableProviderTestService();
        var panel = CreateBoundPanel(service);

        panel.SendCommand.Execute(null);
        await service.Started.Task;

        Assert.True(panel.IsRunning);
        Assert.True(panel.StopCommand.CanExecute(null));
        panel.StopCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsRunning);

        Assert.Equal("{\"delta\":\"部分\"}", panel.ResponseText);
        Assert.False(panel.HasError);
        Assert.True(panel.SendCommand.CanExecute(null));
        Assert.False(panel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task 高频流式进度会批量刷新且最终内容完整()
    {
        const int deltaCount = 400;
        var service = new BurstProgressProviderTestService(deltaCount);
        var panel = CreateBoundPanel(service);
        var responseRefreshCount = 0;
        panel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProviderTestPanelViewModel.ResponseText))
                Interlocked.Increment(ref responseRefreshCount);
        };

        panel.SendCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsRunning && panel.HasResult);

        Assert.Equal(service.ExpectedResponse, panel.ResponseText);
        Assert.True(responseRefreshCount < deltaCount / 4, $"ResponseText 刷新次数过多：{responseRefreshCount}");
    }

    [Fact]
    public async Task 超大流式响应运行中限制预览但停止后保留完整内容()
    {
        var service = new LargeLiveProgressProviderTestService();
        var panel = CreateBoundPanel(service);

        panel.SendCommand.Execute(null);
        await service.Started.Task;
        await WaitUntilAsync(() => panel.ResponseText.Length > 0);

        Assert.InRange(panel.ResponseText.Length, 1, 32_768);
        Assert.True(panel.StopCommand.CanExecute(null));
        panel.StopCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsRunning);

        Assert.Equal(service.ExpectedResponse, panel.ResponseText);
        Assert.True(panel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task 同步高频流不会阻塞发送命令且停止后可以再次发送()
    {
        var service = new BlockingProgressProviderTestService();
        var panel = CreateBoundPanel(service);
        var stopwatch = Stopwatch.StartNew();

        panel.SendCommand.Execute(null);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250), $"发送命令被阻塞 {stopwatch.ElapsedMilliseconds}ms");
        await service.FirstStarted.Task;
        Assert.True(panel.StopCommand.CanExecute(null));
        panel.StopCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsRunning);
        Assert.NotEmpty(panel.ResponseText);

        panel.SendCommand.Execute(null);
        await WaitUntilAsync(() => service.ExecutionCount == 2 && !panel.IsRunning && panel.HasResult);

        Assert.Equal(BlockingProgressProviderTestService.SecondResponse, panel.ResponseText);
        Assert.True(panel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task 请求完成后发送命令可以再次执行()
    {
        var service = new StubProviderTestService();
        var panel = new ProviderTestPanelViewModel(service);
        var provider = new ProviderEditorViewModel
        {
            BusinessId = "p",
            BaseUrl = "https://example.com",
            ApiMode = "openai",
            EndpointFormat = "responses",
        };
        provider.Models.Add(new ModelEditorViewModel { ModelId = "m", Enabled = true });
        panel.BindProvider(provider);

        panel.SendCommand.Execute(null);
        await WaitUntilAsync(() => service.ExecutionCount == 1 && !panel.IsRunning);

        Assert.True(panel.SendCommand.CanExecute(null));
        panel.SendCommand.Execute(null);
        await WaitUntilAsync(() => service.ExecutionCount == 2 && !panel.IsRunning);

        Assert.True(panel.SendCommand.CanExecute(null));
    }
    [Fact]
    public void ResponseContentStateIsSeparateFromCompletedResultState()
    {
        var property = typeof(ProviderTestPanelViewModel).GetProperty("HasResponseText");

        Assert.NotNull(property);

        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());

        Assert.False((bool)property!.GetValue(panel)!);
    }
    [Fact]
    public async Task 清空和发送生命周期()
    {
        var service = new StubProviderTestService();
        var panel = new ProviderTestPanelViewModel(service);
        var provider = new ProviderEditorViewModel { BusinessId = "p", BaseUrl = "https://example.com", ApiMode = "openai", EndpointFormat = "responses", ApiKey = "key" };
        provider.Models.Add(new ModelEditorViewModel { ModelId = "m", Enabled = true });
        panel.BindProvider(provider);
        panel.SendCommand.Execute(null);
        await service.Completed.Task;
        await WaitUntilAsync(() => panel.HasResult);
        Assert.True(panel.HasResult);
        Assert.Equal("答复", panel.ResponseText);
        Assert.True(panel.DeleteResponseSelection(0, panel.ResponseText.Length));
        Assert.Empty(panel.ResponseText);
        Assert.False(panel.HasResult);
    }

    [Theory]
    [InlineData(1, 4, "aef")]
    [InlineData(4, 1, "aef")]
    [InlineData(0, 6, "")]
    public async Task Response可删除任意正向或反向选区(int selectionStart, int selectionEnd, string expected)
    {
        var panel = CreateBoundPanel(new StubProviderTestService("abcdef"));
        panel.SendCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsRunning && panel.HasResult);

        Assert.True(panel.DeleteResponseSelection(selectionStart, selectionEnd));
        Assert.Equal(expected, panel.ResponseText);
        Assert.Equal(expected.Length > 0, panel.HasResponseText);
    }

    [Fact]
    public async Task Response空选区或请求运行中不删除()
    {
        var completedPanel = CreateBoundPanel(new StubProviderTestService("abcdef"));
        completedPanel.SendCommand.Execute(null);
        await WaitUntilAsync(() => !completedPanel.IsRunning && completedPanel.HasResult);
        Assert.False(completedPanel.DeleteResponseSelection(2, 2));
        Assert.Equal("abcdef", completedPanel.ResponseText);

        var service = new CancellableProviderTestService();
        var runningPanel = CreateBoundPanel(service);
        runningPanel.SendCommand.Execute(null);
        await service.Started.Task;
        Assert.False(runningPanel.DeleteResponseSelection(0, 1));
        runningPanel.StopCommand.Execute(null);
        await WaitUntilAsync(() => !runningPanel.IsRunning);
    }

    private static ProviderTestPanelViewModel CreateBoundPanel(IProviderTestService service)
    {
        var panel = new ProviderTestPanelViewModel(service);
        var provider = new ProviderEditorViewModel
        {
            BusinessId = "p",
            BaseUrl = "https://example.com",
            ApiMode = "openai",
            EndpointFormat = "responses",
            ApiKey = "key",
        };
        provider.Models.Add(new ModelEditorViewModel { ModelId = "m", Enabled = true });
        panel.BindProvider(provider);
        return panel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }
    private sealed class StubProviderTestService(string responseText = "答复") : IProviderTestService
    {
        public int ExecutionCount { get; private set; }
        public TaskCompletionSource<ProviderTestResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProviderTestResult> ExecuteAsync(ProviderTestRequest request, IProgress<ProviderTestProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            progress?.Report(new ProviderTestProgress(request.RequestId, ProviderTestStatus.Sending));
            var result = new ProviderTestResult(request.RequestId, ProviderTestStatus.Completed, new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "openai_responses", "/responses", request.Mode, false, "direct", null, 0), 200, "application/json", 1, 10, responseText);
            Completed.TrySetResult(result);
            return Task.FromResult(result);
        }
    }

    private sealed class BurstProgressProviderTestService : IProviderTestService
    {
        private readonly int deltaCount;

        public BurstProgressProviderTestService(int deltaCount)
        {
            this.deltaCount = deltaCount;
            ExpectedResponse = string.Concat(Enumerable.Range(0, deltaCount).Select(index => $"{index}\n"));
        }

        public string ExpectedResponse { get; }

        public async Task<ProviderTestResult> ExecuteAsync(
            ProviderTestRequest request,
            IProgress<ProviderTestProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < deltaCount; index++)
            {
                var delta = $"{index}\n";
                progress?.Report(new ProviderTestProgress(
                    request.RequestId,
                    ProviderTestStatus.Sending,
                    delta));
                await Task.Yield();
            }

            return CreateResult(request, ProviderTestStatus.Completed, ExpectedResponse);
        }
    }

    private sealed class LargeLiveProgressProviderTestService : IProviderTestService
    {
        private const int ChunkSize = 1_000;

        public string ExpectedResponse { get; } = new('x', 200_000);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProviderTestResult> ExecuteAsync(
            ProviderTestRequest request,
            IProgress<ProviderTestProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            for (var offset = 0; offset < ExpectedResponse.Length; offset += ChunkSize)
            {
                progress?.Report(new ProviderTestProgress(
                    request.RequestId,
                    ProviderTestStatus.Sending,
                    ExpectedResponse.Substring(offset, Math.Min(ChunkSize, ExpectedResponse.Length - offset))));
            }

            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return CreateResult(request, ProviderTestStatus.Cancelled, ExpectedResponse, "cancelled");
        }
    }

    private sealed class BlockingProgressProviderTestService : IProviderTestService
    {
        public const string SecondResponse = "{\"done\":true}";
        private int executionCount;

        public int ExecutionCount => Volatile.Read(ref executionCount);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProviderTestResult> ExecuteAsync(
            ProviderTestRequest request,
            IProgress<ProviderTestProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var execution = Interlocked.Increment(ref executionCount);
            if (execution > 1)
                return Task.FromResult(CreateResult(request, ProviderTestStatus.Completed, SecondResponse));

            FirstStarted.TrySetResult();
            var response = new StringBuilder();
            var safetyTimeout = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested && safetyTimeout.Elapsed < TimeSpan.FromMilliseconds(750))
            {
                response.Append('x');
                progress?.Report(new ProviderTestProgress(
                    request.RequestId,
                    ProviderTestStatus.Sending,
                    "x"));
                Thread.SpinWait(2_000);
            }

            return Task.FromResult(CreateResult(request, ProviderTestStatus.Cancelled, response.ToString(), "cancelled"));
        }
    }

    private static ProviderTestResult CreateResult(
        ProviderTestRequest request,
        ProviderTestStatus status,
        string responseText,
        string? errorCode = null)
        => new(
            request.RequestId,
            status,
            new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "openai_responses", "/responses", request.Mode, false, "direct", null, 0),
            200,
            "text/event-stream",
            1,
            responseText.Length,
            responseText,
            ErrorCode: errorCode);

    private sealed class CancellableProviderTestService : IProviderTestService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProviderTestResult> ExecuteAsync(
            ProviderTestRequest request,
            IProgress<ProviderTestProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            const string partialResponse = "{\"delta\":\"部分\"}";
            progress?.Report(new ProviderTestProgress(
                request.RequestId,
                ProviderTestStatus.Sending,
                partialResponse,
                partialResponse.Length));
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return new ProviderTestResult(
                request.RequestId,
                ProviderTestStatus.Cancelled,
                new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "openai_responses", "/responses", request.Mode, false, "direct", null, 0),
                200,
                "text/event-stream",
                1,
                partialResponse.Length,
                partialResponse,
                ErrorCode: "cancelled");
        }
    }
}
