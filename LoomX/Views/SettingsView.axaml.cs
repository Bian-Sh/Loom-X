using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace LoomX.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    private void ReleaseVersion_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is Control { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
            OpenLink(uri.AbsoluteUri);
    }

    private void ProjectHomeButton_OnClick(object? sender, RoutedEventArgs e) => OpenLink("https://github.com/Bian-Sh/Loom-X");
    private void IssuesButton_OnClick(object? sender, RoutedEventArgs e) => OpenLink("https://github.com/Bian-Sh/Loom-X/issues");
}
