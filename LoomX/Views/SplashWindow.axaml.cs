using Avalonia;
using Avalonia.Controls;

namespace LoomX.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        // 构造即接入应用级材质协调点：TransparencyLevelHint 在 Show 之前设好，第一帧即目标材质。
        (Application.Current as App)?.AppearanceCoordinator.ApplyTo(this);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
