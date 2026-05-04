using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComCross.Shared.Models;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MessageStreamView : BaseUserControl
{
    private INotifyCollectionChanged? _currentMessages;
    private int _messageSubscriptionVersion;
    private bool _isDetached;

    public MessageStreamView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isDetached = false;
        AttachCurrentMessages();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachCurrentMessages();
        _isDetached = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachCurrentMessages();
    }

    private void AttachCurrentMessages()
    {
        DetachCurrentMessages();

        if (_isDetached)
        {
            return;
        }

        if (DataContext is MessageStreamViewModel vm)
        {
            _currentMessages = vm.MessageItems;
            _currentMessages.CollectionChanged += OnMessagesChanged;
            unchecked
            {
                _messageSubscriptionVersion++;
            }
        }
    }

    private void DetachCurrentMessages()
    {
        if (_currentMessages != null)
        {
            _currentMessages.CollectionChanged -= OnMessagesChanged;
            _currentMessages = null;
            unchecked
            {
                _messageSubscriptionVersion++;
            }
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var version = _messageSubscriptionVersion;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnMessagesChangedOnUiThread(sender, version));
            return;
        }

        OnMessagesChangedOnUiThread(sender, version);
    }

    private void OnMessagesChangedOnUiThread(object? sender, int version)
    {
        if (_isDetached
            || version != _messageSubscriptionVersion
            || !ReferenceEquals(sender, _currentMessages))
        {
            return;
        }

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
            ScrollToLatestMessage(vm, sender, version);
        });
    }

    private void ScrollToLatestMessage(MessageStreamViewModel vm, object? sender, int version)
    {
        if (_isDetached
            || version != _messageSubscriptionVersion
            || !ReferenceEquals(sender, _currentMessages)
            || !ReferenceEquals(DataContext, vm)
            || !vm.Display.AutoScrollEnabled
            || vm.MessageItems.Count == 0)
        {
            return;
        }

        var latest = vm.MessageItems.LastOrDefault();
        if (latest is not null)
        {
            MessageList.ScrollIntoView(latest);
        }
    }

    private void OnDisplayModeToggleRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.ToggleDisplayMode();
            e.Handled = true;
        }
    }

    private void OnOpenSessionDetailClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel vm)
        {
            vm.OpenSessionDetail(vm.ActiveSession);
        }
    }

    private void OnToggleRightDockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel vm)
        {
            vm.ToggleRightToolDock();
        }
    }

    private void OnToggleMetricsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.ToggleMetricsBar();
        }
    }

    private void OnExportMessagesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open(button);
        }
    }

    private void OnExportPlainClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ExportMessages(SessionLogExportFormat.Plain);

    private void OnExportSlimClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ExportMessages(SessionLogExportFormat.Slim);

    private void OnExportDetailedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ExportMessages(SessionLogExportFormat.DetailedJsonLines);

    private void ExportMessages(SessionLogExportFormat format)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel vm)
        {
            _ = vm.ExportAsync(format: format);
        }
    }

    private async void OnArchiveSwitchToggleRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MessageStreamViewModel vm)
        {
            return;
        }

        var owner = ShellContext.GetOwner(this);
        if (owner is null)
        {
            return;
        }

        var enable = !vm.IsArchiveWriting;
        var confirmed = await ShellContext.Dialogs.ShowConfirmAsync(
            owner,
            enable ? vm.L["session.archive.enable.title"] : vm.L["session.archive.stop.title"],
            enable ? vm.L["session.archive.enable.message"] : vm.L["session.archive.stop.message"],
            MessageBoxIcon.Warning);
        if (!confirmed)
        {
            e.Handled = true;
            return;
        }

        await vm.SetArchiveWritingAsync(enable);
        e.Handled = true;
    }

    private void OnClearMessagesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel vm)
        {
            vm.ClearMessages();
        }
    }
}
