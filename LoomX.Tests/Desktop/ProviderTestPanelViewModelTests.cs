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
        var panel = new ProviderTestPanelViewModel(new StubProviderTestService());
        panel.BindProvider(provider);
        Assert.Equal("enabled", panel.SelectedModel?.ModelId);
        Assert.True(panel.CanSend);
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
        panel.ClearCommand.Execute(null);
        Assert.Empty(panel.ResponseText);
        Assert.False(panel.HasResult);
    }

    private sealed class StubProviderTestService : IProviderTestService
    {
        public TaskCompletionSource<ProviderTestResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProviderTestResult> ExecuteAsync(ProviderTestRequest request, IProgress<ProviderTestProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new ProviderTestProgress(request.RequestId, ProviderTestStatus.Sending));
            var result = new ProviderTestResult(request.RequestId, ProviderTestStatus.Completed, new ProviderTestSummary(request.RequestId, request.ProviderId, request.ModelId, "openai_responses", "/responses", request.Mode, false, "direct", null, 0), 200, "application/json", 1, 10, "答复");
            Completed.TrySetResult(result);
            return Task.FromResult(result);
        }
    }
}
