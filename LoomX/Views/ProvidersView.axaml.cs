using Avalonia.Controls;
using Avalonia.Interactivity;
using LoomX;
using LoomX.ViewModels;

namespace LoomX.Views;
public partial class ProvidersView : UserControl
{
    public ProvidersView() => InitializeComponent();

    private void AddHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel viewModel || viewModel.SelectedProvider is null) return;
        viewModel.SelectedProvider.AddHeader();
    }

    private void RemoveHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HeaderEditorViewModel header } || DataContext is not ProvidersViewModel viewModel || viewModel.SelectedProvider is null) return;
        viewModel.SelectedProvider.RemoveHeader(header);
    }

    private void ToggleApiKeyVisibilityButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProvidersViewModel viewModel)
            viewModel.SelectedProvider?.ToggleApiKeyVisibility();
    }

    private async void DeleteProviderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderEditorViewModel provider } || DataContext is not ProvidersViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;

        var dialog = new GlassDialogWindow
        {
            Title = "提示",
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 76, Classes = { "dialog-action" } };
        var deleteButton = new Button { Content = "删除", MinWidth = 76, Classes = { "dialog-action", "dialog-danger" } };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 12 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);
        dialog.DialogContent = new TextBlock
        {
            Text = $"确定删除 Provider“{provider.DisplayName}”吗？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            MaxWidth = 340,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        dialog.DialogActions = buttons;
        cancelButton.Click += (_, _) => dialog.Close(false);
        deleteButton.Click += (_, _) => dialog.Close(true);
        owner.AppearanceCoordinator.ApplyTo(dialog);

        if (await dialog.ShowDialog<bool>(owner)) viewModel.DeleteProviderCommand.Execute(provider);
    }
}
