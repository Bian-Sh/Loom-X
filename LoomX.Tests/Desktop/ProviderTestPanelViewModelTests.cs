using System.Collections.ObjectModel;
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
    public void 面板不公开停止和重试命令()
    {
        var properties = typeof(ProviderTestPanelViewModel).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("StopCommand", properties);
        Assert.DoesNotContain("RetryCommand", properties);
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
        Assert.True(panel.HasResult);
        Assert.Equal("答复", panel.ResponseText);
        Assert.True(panel.ClearResponse());
        Assert.Empty(panel.ResponseText);
        Assert.False(panel.HasResult);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }
    private sealed class StubProviderTestService : IProviderTestService
    {
        public int ExecutionCount { get; private set; }
        public TaskCompletionSource<ProviderTestResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProviderTestResult> ExecuteAsync(ProviderTestRequest request, IProgress<ProviderTestProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            progress?.Report(new ProviderTestProgress(request.RequestId, ProviderTestStatus.Sending));
            var result = new ProviderTestResult(request.RequestId, ProviderTestStatus.Completed, new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "openai_responses", "/responses", request.Mode, false, "direct", null, 0), 200, "application/json", 1, 10, "答复");
            Completed.TrySetResult(result);
            return Task.FromResult(result);
        }
    }
}
