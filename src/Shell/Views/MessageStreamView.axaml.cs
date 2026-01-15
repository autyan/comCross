using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MessageStreamView : UserControl
{
    public static readonly StyledProperty<string?> TimestampFormatProperty =
        AvaloniaProperty.Register<MessageStreamView, string?>(nameof(TimestampFormat));

    public static readonly StyledProperty<string> MessageFontFamilyProperty =
        AvaloniaProperty.Register<MessageStreamView, string>(nameof(MessageFontFamily), "Consolas");

    public static readonly StyledProperty<int> MessageFontSizeProperty =
        AvaloniaProperty.Register<MessageStreamView, int>(nameof(MessageFontSize), 11);

    private INotifyCollectionChanged? _currentMessages;

    public string? TimestampFormat
    {
        get => GetValue(TimestampFormatProperty);
        set => SetValue(TimestampFormatProperty, value);
    }

    public string MessageFontFamily
    {
        get => GetValue(MessageFontFamilyProperty);
        set => SetValue(MessageFontFamilyProperty, value);
    }

    public int MessageFontSize
    {
        get => GetValue(MessageFontSizeProperty);
        set => SetValue(MessageFontSizeProperty, value);
    }

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

        if (DataContext is MainWindowViewModel vm)
        {
            _currentMessages = vm.Messages;
            _currentMessages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (!vm.AutoScrollEnabled || vm.Messages.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            MessageList.ScrollIntoView(vm.Messages.Last());
        });
    }
}
