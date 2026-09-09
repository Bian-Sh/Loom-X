using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using LoomX.ViewModels;

namespace LoomX.Views;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookAutoScroll();
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
}
