using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LoomX.Assistant;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class AssistantView : UserControl
{
    private const double BottomThreshold = 4;
    private AssistantViewModel? observedModel;
    private bool following = true;
    private bool scrollingToBottom;
    private double lastOffsetY;

    public AssistantView()
    {
        InitializeComponent();
        MessageScroll.ScrollChanged += MessageScroll_OnScrollChanged;
        DataContextChanged += (_, _) =>
        {
            modelPopup.DataContext = DataContext;
            historyPopup.DataContext = DataContext;
            HookAutoScroll();
        };
        modelPopup.DataContext = DataContext;
        historyPopup.DataContext = DataContext;
        HookAutoScroll();
    }

    private void HookAutoScroll()
    {
        if (observedModel is not null) observedModel.Messages.CollectionChanged -= OnMessagesChanged;
        observedModel = DataContext as AssistantViewModel;
        if (observedModel is not null) observedModel.Messages.CollectionChanged += OnMessagesChanged;
        following = true;
        ScrollToLatest();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) following = true;
        if (following) ScrollToLatest();
    }

    private void MessageScroll_OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (scrollingToBottom) return;
        var max = Math.Max(0, MessageScroll.Extent.Height - MessageScroll.Viewport.Height);
        var offset = MessageScroll.Offset.Y;
        if (offset >= max - BottomThreshold) following = true;
        else if (Math.Abs(offset - lastOffsetY) > 0.5) following = false;
        lastOffsetY = offset;
        JumpButton.IsVisible = !following;
        if (following && Math.Abs(args.ExtentDelta.Y) > 0.5) ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!following) return;
            scrollingToBottom = true;
            var max = Math.Max(0, MessageScroll.Extent.Height - MessageScroll.Viewport.Height);
            MessageScroll.Offset = new Vector(MessageScroll.Offset.X, max);
            lastOffsetY = MessageScroll.Offset.Y;
            JumpButton.IsVisible = false;
            scrollingToBottom = false;
        }, DispatcherPriority.Render);
    }

    private void JumpButton_OnClick(object? sender, RoutedEventArgs args)
    {
        following = true;
        ScrollToLatest();
    }

    private async void CopyMessage_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ChatMessageViewModel message }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(message.Text);
        (DataContext as AssistantViewModel)?.NotifyCopied();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is AssistantViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
            args.Handled = true;
        }
    }

    /// <summary>标题区历史会话图标：切换浮层，并在打开前刷新列表。</summary>
    private void HistoryButton_OnClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not AssistantViewModel viewModel) return;
        var next = !viewModel.IsHistoryOpen;
        if (next) viewModel.RefreshSessions();
        viewModel.IsHistoryOpen = next;
    }

    /// <summary>历史会话项主体：载入该会话（删除按钮独立处理，不走到这里）。</summary>
    private void LoadHistoryItem_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: AssistantSessionSummary summary }) return;
        if (DataContext is not AssistantViewModel viewModel) return;
        viewModel.IsHistoryOpen = false;
        if (viewModel.LoadSessionCommand.CanExecute(summary)) viewModel.LoadSessionCommand.Execute(summary);
    }

    /// <summary>过程块（思考 / 处理步骤）折叠与展开。</summary>
    private void ToggleProcess_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: ChatMessageViewModel message }) message.IsExpanded = !message.IsExpanded;
    }

    /// <summary>输入框获得焦点：整张输入卡边框高亮（取代中间的分割线）。</summary>
    private void InputTextBox_OnGotFocus(object? sender, GotFocusEventArgs args)
    {
        if (!inputCard.Classes.Contains("focused")) inputCard.Classes.Add("focused");
    }

    private void InputTextBox_OnLostFocus(object? sender, RoutedEventArgs args)
    {
        inputCard.Classes.Remove("focused");
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

}
