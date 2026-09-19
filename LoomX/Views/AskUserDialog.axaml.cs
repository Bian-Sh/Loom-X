using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class AskUserDialog : Window
{
    public AskUserDialog() => InitializeComponent();

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void SubmitButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AskUserDialogViewModel viewModel && viewModel.TryBuildResult(out _))
        {
            Close(true);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is AskUserDialogViewModel { AllowCancel: true })
        {
            Close(false);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
