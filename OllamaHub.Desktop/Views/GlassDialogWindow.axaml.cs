using Avalonia.Controls;
using Avalonia.Input;

namespace OllamaHub.Desktop.Views;

public partial class GlassDialogWindow : Window
{
    public GlassDialogWindow() => InitializeComponent();

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
