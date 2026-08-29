using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace OllamaHub.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    private void ProjectHomeButton_OnClick(object? sender, RoutedEventArgs e) => OpenLink("https://github.com/mingkuang-Chuyu/OllamaHub");
    private void IssuesButton_OnClick(object? sender, RoutedEventArgs e) => OpenLink("https://github.com/mingkuang-Chuyu/OllamaHub/issues");
}
