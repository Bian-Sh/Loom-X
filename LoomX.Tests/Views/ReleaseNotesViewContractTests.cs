using System.IO;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class ReleaseNotesViewContractTests
{
    [Fact]
    public void 共享视图使用可折叠模块和旧正文回退()
    {
        var source = ReadDesktopFile("Views", "ReleaseNotesView.axaml");
        var codeBehind = ReadDesktopFile("Views", "ReleaseNotesView.axaml.cs");
        var componentSource = source + codeBehind;

        Assert.Contains("xmlns:md=\"using:LiveMarkdown.Avalonia\"", source, StringComparison.Ordinal);
        Assert.Contains("xmlns:vm=\"using:LoomX.ViewModels\"", source, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:ReleaseNotesContentViewModel\"", source, StringComparison.Ordinal);
        Assert.Contains("MarkdownBuilder=\"{Binding Markdown}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", source, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Process.Start", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Launcher", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenUri", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", componentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Markdig", componentSource, StringComparison.Ordinal);
    }

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
