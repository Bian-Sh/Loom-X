using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace LoomX.Views;

public partial class GlassDialogWindow : Window
{
    public GlassDialogWindow()
    {
        InitializeComponent();
        // 构造即接入应用级材质协调点，所有玻璃对话框共享当前外观快照。
        (Application.Current as App)?.AppearanceCoordinator.ApplyTo(this);
    }

    public object? DialogContent
    {
        get => dialogContentPresenter.Content;
        set => dialogContentPresenter.Content = value;
    }

    public object? DialogActions
    {
        get => dialogActionsPresenter.Content;
        set => dialogActionsPresenter.Content = value;
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
