using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class AskUserDialog : Window
{
    private static readonly TimeSpan SingleChoiceAdvanceDelay = TimeSpan.FromMilliseconds(180);

    public AskUserDialog() => InitializeComponent();

    private AskUserDialogViewModel? ViewModel => DataContext as AskUserDialogViewModel;

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { AllowCancel: true })
        {
            Close(false);
        }
    }

    private void PreviousButton_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.MovePrevious();

    private void NextButton_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.MoveNextWithoutValidation();

    private void SkipButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || !viewModel.TrySkipCurrentField(out var shouldSubmit))
        {
            return;
        }

        if (shouldSubmit && viewModel.TryBuildResult(out _))
        {
            Close(true);
        }
    }

    private void PrimaryButton_OnClick(object? sender, RoutedEventArgs e) =>
        AdvanceOrSubmit();

    private async void SingleChoice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true }
            || ViewModel is not { HasNextField: true } viewModel)
        {
            return;
        }

        var fieldIndex = viewModel.CurrentFieldIndex;
        var field = viewModel.CurrentField;
        await Task.Delay(SingleChoiceAdvanceDelay);

        if (!IsVisible
            || !ReferenceEquals(DataContext, viewModel)
            || viewModel.CurrentFieldIndex != fieldIndex
            || !ReferenceEquals(viewModel.CurrentField, field))
        {
            return;
        }

        viewModel.TryAdvanceCurrentField();
    }

    private void TextInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not TextBox { DataContext: AskUserTextFieldViewModel field })
        {
            return;
        }

        var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if ((field.IsMultiline && controlPressed) || (!field.IsMultiline && !controlPressed))
        {
            AdvanceOrSubmit();
            e.Handled = true;
        }
    }

    private void NumberInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            AdvanceOrSubmit();
            e.Handled = true;
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { AllowCancel: true })
        {
            Close(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ViewModel is { } viewModel)
        {
            var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shouldAdvance = viewModel.CurrentField switch
            {
                AskUserTextFieldViewModel { IsMultiline: true } => controlPressed,
                AskUserTextFieldViewModel => !controlPressed,
                AskUserNumberFieldViewModel => !controlPressed,
                _ => false,
            };

            if (shouldAdvance)
            {
                AdvanceOrSubmit();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private void AdvanceOrSubmit()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var shouldSubmit = viewModel.IsLastField;
        if (!viewModel.TryAdvanceCurrentField())
        {
            return;
        }

        if (shouldSubmit && viewModel.TryBuildResult(out _))
        {
            Close(true);
        }
    }
}

