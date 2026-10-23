using Avalonia;
using Avalonia.Controls;

namespace LoomX.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
