using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop;
public partial class MainWindow : Window
{
    private readonly ToastService toastService;
    private readonly DispatcherTimer toastTimer;

    public ToastService ToastService => toastService;

    public MainWindow() : this(new ToastService()) { }

    public MainWindow(ToastService toastService)
    {
        this.toastService = toastService;
        InitializeComponent();
        toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            toastBorder.IsVisible = false;
        };
        toastService.Requested += ToastServiceOnRequested;
        Closed += (_, _) => toastService.Requested -= ToastServiceOnRequested;
    }

    private void ToastServiceOnRequested(object? sender, ToastNotification notification)
    {
        void ShowToast()
        {
            toastText.Text = notification.Message;
            toastBorder.Background = new SolidColorBrush(notification.Level switch
            {
                ToastLevel.Success => Color.Parse("#176B5B"),
                ToastLevel.Warning => Color.Parse("#8A5A12"),
                ToastLevel.Error => Color.Parse("#9E3544"),
                _ => Color.Parse("#17212B")
            });
            toastBorder.IsVisible = true;
            toastTimer.Stop();
            toastTimer.Start();
        }

        if (Dispatcher.UIThread.CheckAccess()) ShowToast();
        else Dispatcher.UIThread.Post(ShowToast);
    }

    private void WindowChrome_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object? sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
