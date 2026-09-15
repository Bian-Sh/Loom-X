using System.Xml.Linq;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class AssistantErrorBubbleTests
{
    [Fact]
    public void 异常正文使用大字号普通字重并且没有手动操作()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LoomX.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "LoomX", "Views", "AssistantView.axaml"));
        var bubble = Assert.Single(document.Descendants(), item => item.Name.LocalName == "Border" && (string?)item.Attribute("IsVisible") == "{Binding IsError}");
        Assert.Equal("{DynamicResource DangerSoftBrush}", (string?)bubble.Attribute("Background"));
        Assert.Equal("18,18,18,6", (string?)bubble.Attribute("CornerRadius"));
        Assert.DoesNotContain(bubble.Descendants(), item => item.Name.LocalName == "Button");
        var text = Assert.Single(bubble.Descendants(), item => (string?)item.Attribute("Text") == "{Binding Text}");
        Assert.Equal("14", (string?)text.Attribute("FontSize"));
        Assert.Equal("Normal", (string?)text.Attribute("FontWeight"));
        Assert.Equal("22", (string?)text.Attribute("LineHeight"));
        Assert.Equal("{DynamicResource DangerMessageTextBrush}", (string?)text.Attribute("Foreground"));
        Assert.Null(typeof(LoomX.ViewModels.AssistantViewModel).GetProperty("RetryCommand"));
        Assert.Null(typeof(LoomX.ViewModels.AssistantViewModel).GetProperty("DismissErrorCommand"));
    }
}
