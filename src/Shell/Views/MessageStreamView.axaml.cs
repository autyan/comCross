using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using ComCross.Shared.Models;
using ComCross.Shell.Services;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class MessageStreamView : BaseUserControl
{
    private INotifyCollectionChanged? _currentMessages;
    private MessageStreamViewModel? _currentViewModel;
    private int _messageSubscriptionVersion;
    private bool _isDetached = true;
    private int _aggregateRefreshVersion;
    private AggregateEditorStateKey? _activeAggregateStateKey;
    private readonly SlimDirectionMargin _slimDirectionMargin = new();
    private readonly Dictionary<AggregateEditorStateKey, AggregateEditorState> _aggregateEditorStates = new();

    public MessageStreamView()
    {
        InitializeComponent();
        ConfigureAggregateEditors();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isDetached = false;
        AttachCurrentMessages();
        ScheduleCurrentAggregateEditorRefresh(forceDocumentReset: true);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        SaveActiveAggregateEditorState();
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
            _currentViewModel = vm;
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _currentMessages = vm.MessageItems;
            _currentMessages.CollectionChanged += OnMessagesChanged;
            RefreshAggregateEditorText(vm);
            ScheduleAggregateEditorRefresh(vm, forceDocumentReset: true);
            unchecked
            {
                _messageSubscriptionVersion++;
            }
        }
    }

    private void DetachCurrentMessages()
    {
        SaveActiveAggregateEditorState();

        if (_currentMessages != null)
        {
            _currentMessages.CollectionChanged -= OnMessagesChanged;
            _currentMessages = null;
            unchecked
            {
                _messageSubscriptionVersion++;
            }
        }

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _currentViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MessageStreamViewModel.AggregateSelectionVersion), StringComparison.Ordinal)
            && sender is MessageStreamViewModel aggregateVm)
        {
            SelectAggregateSearchMatch(aggregateVm);
            return;
        }

        if ((string.Equals(e.PropertyName, nameof(MessageStreamViewModel.AggregateMessageText), StringComparison.Ordinal)
             || string.Equals(e.PropertyName, nameof(MessageStreamViewModel.AggregateDirectionGutterText), StringComparison.Ordinal))
            && sender is MessageStreamViewModel textVm)
        {
            RefreshAggregateEditorText(textVm);
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MessageStreamViewModel.DisplayDensity), StringComparison.Ordinal))
        {
            if (sender is MessageStreamViewModel densityVm)
            {
                RefreshAggregateEditorText(densityVm);
                ScheduleAggregateEditorRefresh(densityVm, forceDocumentReset: true);
            }
        }

        if (string.Equals(e.PropertyName, nameof(MessageStreamViewModel.ReturnToLatestNavigationVersion), StringComparison.Ordinal)
            && sender is MessageStreamViewModel latestVm)
        {
            ScrollToLatestWindow(latestVm);
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(MessageStreamViewModel.SelectedMessageItem), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(MessageStreamViewModel.SelectedMessageNavigationVersion), StringComparison.Ordinal))
        {
            return;
        }

        if (sender is not MessageStreamViewModel vm || vm.SelectedMessageItem is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDetached && ReferenceEquals(DataContext, vm) && vm.SelectedMessageItem is not null)
            {
                ScrollToSelectedMessage(vm);
            }
        }, DispatcherPriority.Loaded);
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

        if (!vm.Display.AutoScrollEnabled
            || vm.MessageItems.Count == 0
            || vm.HasSearchQuery
            || vm.IsReturnToLatestVisible)
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
            || vm.HasSearchQuery
            || vm.IsReturnToLatestVisible
            || vm.MessageItems.Count == 0)
        {
            return;
        }

        var latest = vm.MessageItems.LastOrDefault();
        if (latest is not null)
        {
            MessageList.ScrollIntoView(latest);
        }

        if (vm.IsAggregateTextMode)
        {
            ScrollAggregateToEnd(vm);
        }
    }

    private void ScrollToLatestWindow(MessageStreamViewModel vm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDetached || !ReferenceEquals(DataContext, vm))
            {
                return;
            }

            if (vm.IsAggregateTextMode)
            {
                ScrollAggregateToEnd(vm);
                return;
            }

            if (vm.MessageItems.LastOrDefault() is { } latest)
            {
                MessageList.ScrollIntoView(latest);
            }
        }, DispatcherPriority.Loaded);
    }

    private void ScrollAggregateToEnd(MessageStreamViewModel vm)
    {
        if (_isDetached || !ReferenceEquals(DataContext, vm) || !vm.IsAggregateTextMode)
        {
            return;
        }

        var editor = GetActiveAggregateEditor();
        if (editor is null)
        {
            return;
        }

        editor.UpdateLayout();
        var textLength = GetEditorTextLength(editor);
        editor.CaretOffset = textLength;
        editor.SelectionStart = textLength;
        editor.SelectionLength = 0;
        ScrollAggregateEditorToEnd(editor);
        SaveActiveAggregateEditorState();

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDetached || !ReferenceEquals(DataContext, vm) || !vm.IsAggregateTextMode)
            {
                return;
            }

            var currentEditor = GetActiveAggregateEditor();
            if (currentEditor is null)
            {
                return;
            }

            currentEditor.UpdateLayout();
            ScrollAggregateEditorToEnd(currentEditor);
        }, DispatcherPriority.Render);
    }

    private void ScrollToSelectedMessage(MessageStreamViewModel vm)
    {
        if (_isDetached || !ReferenceEquals(DataContext, vm) || vm.SelectedMessageItem is null)
        {
            return;
        }

        MessageList.UpdateLayout();
        MessageList.ScrollIntoView(vm.SelectedMessageItem);

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDetached || !ReferenceEquals(DataContext, vm) || vm.SelectedMessageItem is null)
            {
                return;
            }

            MessageList.UpdateLayout();
            MessageList.ScrollIntoView(vm.SelectedMessageItem);
            CenterSelectedMessageContainer(vm);
        }, DispatcherPriority.Render);
    }

    private void CenterSelectedMessageContainer(MessageStreamViewModel vm)
    {
        var selectedItem = vm.SelectedMessageItem;
        if (selectedItem is null || MessageList.ContainerFromItem(selectedItem) is not Control container)
        {
            return;
        }

        var scrollViewer = MessageList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var bounds = container.TranslatePoint(new Point(0, container.Bounds.Height / 2), scrollViewer);
        if (bounds is not { } center)
        {
            return;
        }

        var targetY = scrollViewer.Offset.Y + center.Y - scrollViewer.Viewport.Height / 2;
        targetY = Math.Clamp(targetY, 0, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
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

    private void OnClearSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.ClearSearch();
        }
    }

    private void OnPreviousSearchResultClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.GoToPreviousSearchMatch();
        }
    }

    private void OnNextSearchResultClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.GoToNextSearchMatch();
        }
    }

    private void OnReturnToLatestClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MessageStreamViewModel vm)
        {
            vm.ReturnToLatest();
        }
    }

    private void SelectAggregateSearchMatch(MessageStreamViewModel vm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDetached || !ReferenceEquals(DataContext, vm) || !vm.IsAggregateTextMode)
            {
                return;
            }

            var editor = GetActiveAggregateEditor();
            if (editor is null)
            {
                return;
            }

            var start = Math.Clamp(vm.AggregateSelectionStart, 0, editor.Text?.Length ?? 0);
            var length = Math.Clamp(vm.AggregateSelectionLength, 0, Math.Max(0, (editor.Text?.Length ?? 0) - start));
            editor.SelectionStart = start;
            editor.SelectionLength = length;
            editor.CaretOffset = start + length;
            if (editor.Document is not null)
            {
                editor.ScrollToLine(editor.Document.GetLineByOffset(start).LineNumber);
            }
        });
    }

    private void ConfigureAggregateEditors()
    {
        var textBrush = ResolveBrush("Text0Brush", Brushes.White);
        var mutedBrush = ResolveBrush("Text2Brush", Brushes.Gray);
        var backgroundBrush = ResolveBrush("Bg0Brush", Brushes.Black);
        var selectionBrush = ResolveBrush("SelectionBrush", Brushes.DodgerBlue);

        ConfigureAggregateTextEditor(PlainAggregateMessageEditor, textBrush, backgroundBrush, selectionBrush, showEndOfLine: false);
        ConfigureAggregateTextEditor(SlimAggregateMessageEditor, textBrush, backgroundBrush, selectionBrush, showEndOfLine: false);
        InstallSlimDirectionMargin(mutedBrush);
        WatchAggregateEditorLifecycle(PlainAggregateMessageEditor);
        WatchAggregateEditorLifecycle(SlimAggregateMessageEditor);
    }

    private void InstallSlimDirectionMargin(IBrush textBrush)
    {
        _slimDirectionMargin.SetStyle(
            textBrush,
            ResolveBrush("BorderSubtleBrush", Brushes.Gray),
            SlimAggregateMessageEditor.FontFamily,
            SlimAggregateMessageEditor.FontSize);
        SlimAggregateMessageEditor.TextArea.LeftMargins.Insert(0, _slimDirectionMargin);
    }

    private void RefreshSlimDirectionMarginStyle()
    {
        _slimDirectionMargin.SetStyle(
            ResolveBrush("Text2Brush", Brushes.Gray),
            ResolveBrush("BorderSubtleBrush", Brushes.Gray),
            SlimAggregateMessageEditor.FontFamily,
            SlimAggregateMessageEditor.FontSize);
    }

    private void WatchAggregateEditorLifecycle(TextEditor editor)
    {
        editor.AttachedToVisualTree += OnAggregateEditorAttachedToVisualTree;
        editor.PropertyChanged += OnAggregateEditorPropertyChanged;
    }

    private void OnAggregateEditorAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => ScheduleCurrentAggregateEditorRefresh(forceDocumentReset: true);

    private void OnAggregateEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextEditor { IsVisible: true })
        {
            ScheduleCurrentAggregateEditorRefresh(forceDocumentReset: true);
        }

        if (ReferenceEquals(sender, SlimAggregateMessageEditor)
            && (string.Equals(e.Property.Name, nameof(FontFamily), StringComparison.Ordinal)
                || string.Equals(e.Property.Name, nameof(FontSize), StringComparison.Ordinal)))
        {
            RefreshSlimDirectionMarginStyle();
            _slimDirectionMargin.InvalidateVisual();
        }
    }

    private void ScheduleCurrentAggregateEditorRefresh(bool forceDocumentReset)
    {
        if (!_isDetached && DataContext is MessageStreamViewModel vm)
        {
            ScheduleAggregateEditorRefresh(vm, forceDocumentReset);
        }
    }

    private void ScheduleAggregateEditorRefresh(MessageStreamViewModel vm, bool forceDocumentReset)
    {
        var version = ++_aggregateRefreshVersion;
        Dispatcher.UIThread.Post(
            () => RefreshAggregateEditorTextIfCurrent(vm, version, forceDocumentReset),
            DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(
            () => RefreshAggregateEditorTextIfCurrent(vm, version, forceDocumentReset),
            DispatcherPriority.Render);
    }

    private void RefreshAggregateEditorTextIfCurrent(
        MessageStreamViewModel vm,
        int version,
        bool forceDocumentReset)
    {
        if (version == _aggregateRefreshVersion)
        {
            RefreshAggregateEditorText(vm, forceDocumentReset);
        }
    }

    private void RefreshAggregateEditorText(MessageStreamViewModel vm, bool forceDocumentReset = false)
    {
        if (_isDetached || !ReferenceEquals(DataContext, vm))
        {
            return;
        }

        if (!TryGetAggregateEditorStateKey(vm, out var nextKey))
        {
            SaveActiveAggregateEditorState();
            _activeAggregateStateKey = null;
            return;
        }

        SaveActiveAggregateEditorState();
        _activeAggregateStateKey = nextKey;

        switch (vm.DisplayDensity)
        {
            case MessageDisplayDensity.Plain:
                SetEditorText(PlainAggregateMessageEditor, vm.AggregateMessageText, forceDocumentReset);
                RestoreAggregateEditorState(nextKey);
                break;
            case MessageDisplayDensity.Slim:
                SetEditorText(SlimAggregateMessageEditor, vm.AggregateMessageText, forceDocumentReset);
                RefreshSlimDirectionMarginStyle();
                _slimDirectionMargin.SetLabels(vm.AggregateDirectionGutterText);
                RestoreAggregateEditorState(nextKey);
                break;
        }
    }

    private static void SetEditorText(TextEditor editor, string text, bool forceDocumentReset = false)
    {
        if (!forceDocumentReset && string.Equals(editor.Document?.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var caret = Math.Clamp(editor.CaretOffset, 0, text.Length);
        editor.Document = new TextDocument(text);
        editor.CaretOffset = caret;
        editor.SelectionLength = 0;
    }

    private static bool TryGetAggregateEditorStateKey(
        MessageStreamViewModel vm,
        out AggregateEditorStateKey key)
    {
        if (vm.ActiveSession is { Id.Length: > 0 } session
            && vm.DisplayDensity is MessageDisplayDensity.Plain or MessageDisplayDensity.Slim)
        {
            key = new AggregateEditorStateKey(session.Id, vm.DisplayDensity);
            return true;
        }

        key = default;
        return false;
    }

    private void SaveActiveAggregateEditorState()
    {
        if (_activeAggregateStateKey is { } key)
        {
            SaveAggregateEditorState(key);
        }
    }

    private void SaveAggregateEditorState(AggregateEditorStateKey key)
    {
        var editor = GetAggregateEditor(key.DisplayDensity);
        if (editor is null)
        {
            return;
        }

        var state = GetOrCreateAggregateEditorState(key);
        var textLength = GetEditorTextLength(editor);
        state.CaretOffset = Math.Clamp(editor.CaretOffset, 0, textLength);
        state.SelectionStart = Math.Clamp(editor.SelectionStart, 0, textLength);
        state.SelectionLength = Math.Clamp(editor.SelectionLength, 0, Math.Max(0, textLength - state.SelectionStart));

        if (GetEditorScrollViewer(editor) is { } scrollViewer)
        {
            state.ScrollOffset = scrollViewer.Offset;
        }
    }

    private void RestoreAggregateEditorState(AggregateEditorStateKey key)
    {
        if (!_aggregateEditorStates.TryGetValue(key, out var state))
        {
            return;
        }

        var editor = GetAggregateEditor(key.DisplayDensity);
        if (editor is null)
        {
            return;
        }

        RestoreAggregateEditorSelection(editor, state);
        RestoreAggregateEditorScroll(editor, state.ScrollOffset);

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDetached || !_activeAggregateStateKey.Equals(key))
            {
                return;
            }

            RestoreAggregateEditorSelection(editor, state);
            RestoreAggregateEditorScroll(editor, state.ScrollOffset);
        }, DispatcherPriority.Render);
    }

    private static void RestoreAggregateEditorSelection(TextEditor editor, AggregateEditorState state)
    {
        var textLength = GetEditorTextLength(editor);
        var start = Math.Clamp(state.SelectionStart, 0, textLength);
        var length = Math.Clamp(state.SelectionLength, 0, Math.Max(0, textLength - start));
        editor.SelectionStart = start;
        editor.SelectionLength = length;
        editor.CaretOffset = Math.Clamp(state.CaretOffset, 0, textLength);
    }

    private static void RestoreAggregateEditorScroll(TextEditor editor, Vector offset)
    {
        if (GetEditorScrollViewer(editor) is not { } scrollViewer)
        {
            return;
        }

        scrollViewer.Offset = new Vector(
            Math.Clamp(offset.X, 0, Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width)),
            Math.Clamp(offset.Y, 0, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height)));
    }

    private static void ScrollAggregateEditorToEnd(TextEditor editor)
    {
        if (GetEditorScrollViewer(editor) is not { } scrollViewer)
        {
            editor.ScrollToEnd();
            return;
        }

        editor.UpdateLayout();
        var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (maxY <= 0)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
            return;
        }

        editor.ScrollToEnd();
    }

    private AggregateEditorState GetOrCreateAggregateEditorState(AggregateEditorStateKey key)
    {
        if (!_aggregateEditorStates.TryGetValue(key, out var state))
        {
            state = new AggregateEditorState();
            _aggregateEditorStates[key] = state;
        }

        return state;
    }

    private IBrush ResolveBrush(string key, IBrush fallback)
        => Application.Current is not null
           && Application.Current.TryFindResource(key, out var resource)
           && resource is IBrush brush
            ? brush
            : fallback;

    private static void ConfigureAggregateTextEditor(
        TextEditor editor,
        IBrush foreground,
        IBrush background,
        IBrush selection,
        bool showEndOfLine)
    {
        editor.SyntaxHighlighting = null;
        editor.Foreground = foreground;
        editor.Background = background;
        editor.LineNumbersForeground = foreground;
        editor.TextArea.Foreground = foreground;
        editor.TextArea.Background = background;
        editor.TextArea.SelectionBrush = selection;
        editor.TextArea.SelectionForeground = foreground;
        editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
        editor.TextArea.TextView.LinkTextForegroundBrush = foreground;
        editor.TextArea.TextView.LinkTextBackgroundBrush = Brushes.Transparent;
        editor.TextArea.TextView.NonPrintableCharacterBrush = foreground;
        editor.ShowLineNumbers = false;
        editor.Options.ShowSpaces = false;
        editor.Options.ShowTabs = true;
        editor.Options.ShowEndOfLine = showEndOfLine;
        editor.Options.ShowBoxForControlCharacters = true;
        editor.Options.AllowScrollBelowDocument = false;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableTextDragDrop = false;
        editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
    }

    private TextEditor? GetActiveAggregateEditor()
    {
        if (DataContext is not MessageStreamViewModel vm)
        {
            return null;
        }

        return GetAggregateEditor(vm.DisplayDensity);
    }

    private TextEditor? GetAggregateEditor(MessageDisplayDensity displayDensity)
        => displayDensity switch
        {
            MessageDisplayDensity.Plain => PlainAggregateMessageEditor,
            MessageDisplayDensity.Slim => SlimAggregateMessageEditor,
            _ => null
        };

    private static ScrollViewer? GetEditorScrollViewer(TextEditor editor)
        => editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private static int GetEditorTextLength(TextEditor editor)
        => editor.Document?.TextLength ?? 0;

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

    private readonly record struct AggregateEditorStateKey(string SessionId, MessageDisplayDensity DisplayDensity);

    private sealed class AggregateEditorState
    {
        public int CaretOffset { get; set; }

        public int SelectionStart { get; set; }

        public int SelectionLength { get; set; }

        public Vector ScrollOffset { get; set; }
    }
}
