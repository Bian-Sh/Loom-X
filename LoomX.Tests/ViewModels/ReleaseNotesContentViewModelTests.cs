using System.ComponentModel;
using LoomX.Localization;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.ViewModels;

public sealed class ReleaseNotesContentViewModelTests
{
    [Fact]
    public void Sanitize_移除Html图片和非Https目标()
    {
        var source = """
            # 标题
            <script>alert(1)</script>
            ![图片](https://img.example/a.png)
            [安全](https://example.com) [危险](http://example.com)
            """;

        var result = ReleaseNotesMarkdownPolicy.Sanitize(source);

        Assert.Contains("# 标题", result);
        Assert.Contains("[安全](https://example.com)", result);
        Assert.Contains("图片", result);
        Assert.Contains("危险", result);
        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img.example", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://example.com", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_保留普通Markdown并将本地协议降级为文本()
    {
        var source = """
            ## 改进

            - **更快**
            - [文档](https://example.com/docs)
            - [本地文件](file:///C:/secret.txt)
            """;

        var result = ReleaseNotesMarkdownPolicy.Sanitize(source);

        Assert.Contains("## 改进", result);
        Assert.Contains("- **更快**", result);
        Assert.Contains("[文档](https://example.com/docs)", result);
        Assert.Contains("本地文件", result);
        Assert.DoesNotContain("file:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n")]
    public void Sanitize_空正文返回空字符串(string? source)
    {
        Assert.Equal(string.Empty, ReleaseNotesMarkdownPolicy.Sanitize(source));
    }

    [Fact]
    public void SetRelease_替换版本时创建全新Builder()
    {
        using var vm = new ReleaseNotesContentViewModel();
        vm.SetRelease(CreateRelease("0.12.7", "第一版"));
        var first = vm.Markdown;

        vm.SetRelease(CreateRelease("0.12.8", "第二版"));

        Assert.NotSame(first, vm.Markdown);
        Assert.Equal("第二版", vm.Markdown.ToString());
        Assert.Equal("v0.12.8", vm.Title);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void SetRelease_空正文只进入空态而不把占位文案写入Markdown()
    {
        using var vm = new ReleaseNotesContentViewModel();

        vm.SetRelease(CreateRelease("0.12.7", "<span></span>"));

        Assert.True(vm.IsEmpty);
        Assert.Equal(string.Empty, vm.Markdown.ToString());
        Assert.Equal("release.notes.empty", vm.EmptyText);
    }

    [Fact]
    public void SetRelease_空Release清空全部投影并替换Builder()
    {
        using var vm = new ReleaseNotesContentViewModel();
        vm.SetRelease(CreateRelease("0.12.7", "已有内容"));
        var previous = vm.Markdown;

        vm.SetRelease(null);

        Assert.NotSame(previous, vm.Markdown);
        Assert.Equal(string.Empty, vm.Title);
        Assert.Equal(string.Empty, vm.PublishedAtText);
        Assert.Equal(string.Empty, vm.Markdown.ToString());
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void CultureChanged_重新计算日期和空态文案()
    {
        var previousCulture = LocaleService.CurrentCulture.Name;
        try
        {
            LocaleService.SetCulture("en-US");
            using var vm = new ReleaseNotesContentViewModel();
            vm.SetRelease(CreateRelease("0.12.7", string.Empty, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero)));
            var englishDate = vm.PublishedAtText;
            var notifications = new List<string?>();
            vm.PropertyChanged += OnPropertyChanged;

            LocaleService.SetCulture("zh-CN");

            Assert.NotEqual(englishDate, vm.PublishedAtText);
            Assert.Contains(nameof(ReleaseNotesContentViewModel.PublishedAtText), notifications);
            Assert.Contains(nameof(ReleaseNotesContentViewModel.EmptyText), notifications);

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => notifications.Add(args.PropertyName);
        }
        finally
        {
            LocaleService.SetCulture(previousCulture);
        }
    }

    private static UpdateRelease CreateRelease(
        string version,
        string body,
        DateTimeOffset? publishedAt = null) =>
        new(
            $"v{version}",
            version,
            $"Loom-X {version}",
            body,
            $"https://github.com/Bian-Sh/Loom-X/releases/tag/v{version}",
            publishedAt,
            [],
            null,
            null);
}
