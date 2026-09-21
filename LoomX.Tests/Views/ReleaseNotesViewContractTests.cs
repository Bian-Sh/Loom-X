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
        Assert.Contains("ItemsSource=\"{Binding Sections}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsSectioned}\"", source, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:ReleaseNoteSectionViewModel\"", source, StringComparison.Ordinal);
        Assert.Contains("MarkdownBuilder=\"{Binding HeadingMarkdown}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"{Binding Title}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{Binding IsExpanded}\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Title}\"", source, StringComparison.Ordinal);
        Assert.Contains("MarkdownBuilder=\"{Binding Markdown}\"", source, StringComparison.Ordinal);
        Assert.True(source.Split("<md:MarkdownRenderer", StringSplitOptions.None).Length - 1 >= 2);
        Assert.Equal(1, source.Split("<ScrollViewer", StringSplitOptions.None).Length - 1);
        Assert.Contains("IsVisible=\"{Binding HasUnsectionedContent}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsEmpty}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EmptyText}\"", source, StringComparison.Ordinal);
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
