using System.IO;
using LoomX.Views;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class ReleaseNotesViewContractTests
{
    [Fact]
    public void 共享视图使用单一Markdown渲染并支持外部链接()
    {
        var source = ReadDesktopFile("Views", "ReleaseNotesView.axaml");
        var codeBehind = ReadDesktopFile("Views", "ReleaseNotesView.axaml.cs");
        var componentSource = source + codeBehind;

        Assert.Contains("xmlns:md=\"using:LiveMarkdown.Avalonia\"", source, StringComparison.Ordinal);
        Assert.Contains("xmlns:vm=\"using:LoomX.ViewModels\"", source, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:ReleaseNotesContentViewModel\"", source, StringComparison.Ordinal);
        Assert.Contains("MarkdownBuilder=\"{Binding Markdown}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHitTestVisible=\"False\"", source, StringComparison.Ordinal);
        Assert.Contains("LinkClick=\"MarkdownRenderer_OnLinkClick\"", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("<md:MarkdownRenderer", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, source.Split("<ScrollViewer", StringSplitOptions.None).Length - 1);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasContent}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsEmpty}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EmptyText}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Expander", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding Sections}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView", componentSource, StringComparison.Ordinal);
        Assert.Contains("Process.Start", codeBehind, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Uri.UriSchemeHttp", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Uri.UriSchemeHttps", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Markdig", componentSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 外部链接只允许Http协议并支持相对发布页地址()
    {
        Assert.True(ReleaseNotesView.TryResolveExternalLink(
            new Uri("https://example.com/release"),
            null,
            out var absolute));
        Assert.Equal("https://example.com/release", absolute.AbsoluteUri);

        Assert.True(ReleaseNotesView.TryResolveExternalLink(
            new Uri("/Bian-Sh/Loom-X/commits/v0.12.7", UriKind.Relative),
            "https://github.com/Bian-Sh/Loom-X/releases/tag/v0.12.7",
            out var relative));
        Assert.Equal("https://github.com/Bian-Sh/Loom-X/commits/v0.12.7", relative.AbsoluteUri);

        Assert.False(ReleaseNotesView.TryResolveExternalLink(null, null, out _));
        Assert.False(ReleaseNotesView.TryResolveExternalLink(
            new Uri("file:///tmp/release-notes.txt"),
            null,
            out _));
        Assert.False(ReleaseNotesView.TryResolveExternalLink(
            new Uri("javascript:alert(1)"),
            null,
            out _));
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
