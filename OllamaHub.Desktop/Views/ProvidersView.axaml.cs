using Avalonia.Controls;
using Avalonia.Interactivity;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;
public partial class ProvidersView : UserControl
{
    public ProvidersView() => InitializeComponent();

    private void AddHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProvidersViewModel viewModel && viewModel.SelectedProvider is not null)
            viewModel.SelectedProvider.AddHeader();
    }

    private void RemoveHeaderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HeaderEditorViewModel header } && DataContext is ProvidersViewModel viewModel && viewModel.SelectedProvider is not null)
            viewModel.SelectedProvider.RemoveHeader(header);
    }

    private async void DeleteProviderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderEditorViewModel provider } || DataContext is not ProvidersViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialog = new Window
        {
            Title = "确认删除 Provider",
            Width = 420,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 76 };
        var deleteButton = new Button { Content = "删除", MinWidth = 76 };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"确定删除 Provider“{provider.DisplayName}”吗？", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        cancelButton.Click += (_, _) => dialog.Close(false);
        deleteButton.Click += (_, _) => dialog.Close(true);

        if (await dialog.ShowDialog<bool>(owner)) viewModel.DeleteProviderCommand.Execute(provider);
    }
}
