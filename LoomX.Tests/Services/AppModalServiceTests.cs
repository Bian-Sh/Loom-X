using LoomX.Models;
using LoomX.Services;
using LoomX.Tests.Logging;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class AppModalServiceTests
{
    [Fact]
    public async Task ShowConfirmationAsync公开请求内容并等待显式完成()
    {
        var service = new AppModalService();

        var resultTask = service.ShowConfirmationAsync(
            "删除配置",
            "此操作无法撤销。",
            AppModalKind.Warning,
            "仍要删除",
            "返回");

        Assert.False(resultTask.IsCompleted);
        var request = Assert.IsType<AppModalRequest>(service.CurrentRequest);
        Assert.Equal("删除配置", request.Options.Title);
        Assert.Equal("此操作无法撤销。", request.Options.Message);
        Assert.Equal(AppModalKind.Warning, request.Options.Kind);
        Assert.Equal("仍要删除", request.Options.ConfirmButtonText);
        Assert.Equal("返回", request.Options.CancelButtonText);

        Assert.True(service.TryCompleteCurrent(true));
        Assert.True(await resultTask);
        Assert.Null(service.CurrentRequest);
    }

    [Fact]
    public async Task 多个请求按先进先出顺序逐个显示并返回结果()
    {
        var service = new AppModalService();
        var firstTask = service.ShowConfirmationAsync("第一个", "正文一");
        var secondTask = service.ShowConfirmationAsync("第二个", "正文二");

        Assert.Equal("第一个", service.CurrentRequest?.Options.Title);
        Assert.True(service.TryCompleteCurrent(false));
        Assert.False(await firstTask);
        Assert.Equal("第二个", service.CurrentRequest?.Options.Title);
        Assert.False(secondTask.IsCompleted);

        Assert.True(service.TryCompleteCurrent(true));
        Assert.True(await secondTask);
        Assert.Null(service.CurrentRequest);
    }

    [Fact]
    public async Task 日志记录生命周期但不包含标题正文或按钮文案()
    {
        var logger = new RecordingLogger<AppModalService>();
        var service = new AppModalService(logger);

        var resultTask = service.ShowConfirmationAsync(
            "敏感标题",
            "敏感正文",
            AppModalKind.Warning,
            "敏感确认",
            "敏感取消");
        service.TryCompleteCurrent(true);
        await resultTask;

        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("模态请求已激活", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("模态请求已完成", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("敏感标题", StringComparison.Ordinal)
            || message.Contains("敏感正文", StringComparison.Ordinal)
            || message.Contains("敏感确认", StringComparison.Ordinal)
            || message.Contains("敏感取消", StringComparison.Ordinal));
    }

    [Fact]
    public void 没有活动请求时完成操作返回False()
    {
        var service = new AppModalService();

        Assert.False(service.TryCompleteCurrent(true));
    }

    [Fact]
    public void 默认按钮文本适合通用确认场景()
    {
        var options = new AppModalOptions("标题", "正文");

        Assert.Equal(AppModalKind.Information, options.Kind);
        Assert.Equal("确定", options.ConfirmButtonText);
        Assert.Equal("取消", options.CancelButtonText);
    }
}
