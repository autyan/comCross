using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MessageStreamView : BaseUserControl
{
    private INotifyCollectionChanged? _currentMessages;

    public MessageStreamView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentMessages != null)
        {
            _currentMessages.CollectionChanged -= OnMessagesChanged;
            _currentMessages = null;
        }

        if (DataContext is MessageStreamViewModel vm)
        {
            _currentMessages = vm.MessageItems;
            _currentMessages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not MessageStreamViewModel vm)
        {
            return;
        }

        if (!vm.Display.AutoScrollEnabled || vm.MessageItems.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            MessageList.ScrollIntoView(vm.MessageItems.Last());
        });
    }

    private void OnToggleDisplayMode(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.ToggleDisplayMode();
            e.Handled = true;
        }
    }
}
