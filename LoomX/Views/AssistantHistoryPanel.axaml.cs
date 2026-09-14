using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoomX.ViewModels;

namespace LoomX.Views;

/// <summary>
/// 历史会话面板：承载会话选择以及行内改名（铅笔 → 文本框 → 回车/Esc/失焦）的交互。
/// </summary>
public partial class AssistantHistoryPanel : UserControl
{
    public AssistantHistoryPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebindPanel();
        Loaded += (_, _) => RebindPanel();
    }

    /// <summary>面板要求的 DataContext 不是 AssistantViewModel 时什么都不渲染，避免绑定报错刷屏。</summary>
    private void RebindPanel()
    {
        IsVisible = DataContext is AssistantViewModel;
    }

    /// <summary>主体：载入会话并关掉宿主浮窗。</summary>
    private void LoadItem_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: AssistantSessionItemViewModel item }) return;
        if (DataContext is not AssistantViewModel viewModel) return;
        viewModel.IsHistoryOpen = false;
        if (viewModel.LoadSessionCommand.CanExecute(item)) viewModel.LoadSessionCommand.Execute(item);
    }

    /// <summary>删除：留在浮窗里，允许连续删除。</summary>
    private void DeleteItem_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: AssistantSessionItemViewModel item }) return;
        if (DataContext is not AssistantViewModel viewModel) return;
        viewModel.DeleteSession(item);
    }

    /// <summary>铅笔：进入编辑态并聚焦编辑框。</summary>
    private void BeginRename_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: AssistantSessionItemViewModel item }) return;
        item.BeginRename();

        // 模板结构固定：铅笔的父 Grid 里找编辑框
        if (sender is Button { Parent: Grid grid })
        {
            if (grid.Children.OfType<TextBox>().FirstOrDefault() is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }
    }

    private void RenameBox_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not TextBox { Tag: AssistantSessionItemViewModel item }) return;
        switch (args.Key)
        {
            case Key.Enter:
                args.Handled = true;
                CommitRename((TextBox)sender, item);
                break;
            case Key.Escape:
                args.Handled = true;
                item.CancelRename();
                break;
        }
    }

    private void RenameBox_OnLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not TextBox { Tag: AssistantSessionItemViewModel item }) return;
        CommitRename((TextBox)sender, item);
    }

    private async void CommitRename(TextBox box, AssistantSessionItemViewModel item)
    {
        if (!item.IsEditing) return;
        if (DataContext is not AssistantViewModel viewModel) return;
        await viewModel.RenameSessionAsync(item, box.Text);
    }
}
