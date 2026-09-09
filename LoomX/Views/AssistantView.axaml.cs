using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using LoomX.Assistant;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            modelPopup.DataContext = DataContext;
            HookAutoScroll();
        };
        modelPopup.DataContext = DataContext;
        HookAutoScroll();
    }

    private void HookAutoScroll()
    {
        if (DataContext is AssistantViewModel viewModel)
        {
            viewModel.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            MessageScroll.ScrollToEnd();
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is AssistantViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
            args.Handled = true;
        }
    }

    /// <summary>历史会话项内回收站：删除该会话（不触发选中载入）。</summary>
    private void DeleteHistoryItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is Button { Tag: AssistantSessionSummary summary } && DataContext is AssistantViewModel viewModel)
        {
            viewModel.DeleteSession(summary);
        }
    }

    private async void ModelPicker_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is not AssistantViewModel viewModel) return;
        await viewModel.OpenModelPickerAsync();
        if (viewModel.IsModelPickerOpen)
        {
            modelSearch.Text = string.Empty;
            modelSearch.Focus();
        }
    }

    private void ModelSearch_OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (DataContext is AssistantViewModel viewModel) viewModel.FilterModels(modelSearch.Text);
    }

    private void ToggleProviderGroup_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is Button { Tag: AssistantModelGroupViewModel group }) group.IsExpanded = !group.IsExpanded;
    }

    private void ModelOption_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is Button { Tag: AssistantModelOptionViewModel option } && DataContext is AssistantViewModel viewModel)
        {
            viewModel.SelectModel(option);
        }
    }

    private void ModelAuto_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is AssistantViewModel viewModel) viewModel.SelectAutoModel();
    }
}
