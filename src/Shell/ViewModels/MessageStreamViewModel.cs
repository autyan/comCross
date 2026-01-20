using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

public sealed class MessageStreamViewModel : BaseViewModel
{
    private readonly IMessageStreamService _messageStream;
    private readonly SettingsService _settingsService;
    private readonly IObjectFactory _objectFactory;

    private IDisposable? _messageSubscription;

    private Session? _activeSession;
    private string _searchQuery = string.Empty;

    public MessageStreamViewModel(
        ILocalizationService localization,
        IMessageStreamService messageStream,
        SettingsService settingsService,
        DisplaySettingsViewModel display,
        IObjectFactory objectFactory)
        : base(localization)
    {
        _messageStream = messageStream;
        _settingsService = settingsService;
        Display = display;
        _objectFactory = objectFactory;

        Display.PropertyChanged += OnDisplayPropertyChanged;
    }

    public DisplaySettingsViewModel Display { get; }

    public ObservableCollection<LogMessageListItemViewModel> MessageItems { get; } = new();

    public Session? ActiveSession
    {
        get => _activeSession;
        private set => SetProperty(ref _activeSession, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    public void SetActiveSession(Session? session)
    {
        if (ReferenceEquals(_activeSession, session))
        {
            return;
        }

        ActiveSession = session;

        _messageSubscription?.Dispose();
        _messageSubscription = null;

        LoadMessages();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        var sessionId = _activeSession.Id;
        _messageSubscription = _messageStream.Subscribe(sessionId, message =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeSession?.Id != sessionId)
                {
                    return;
                }

                MessageItems.Add(_objectFactory.Create<LogMessageListItemViewModel>(message, Display.TimestampFormat));
                TrimMessages();
            });
        });
    }

    public void ClearView()
    {
        MessageItems.Clear();
    }

    private void LoadMessages()
    {
        MessageItems.Clear();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        var max = _settingsService.Current.Display.MaxMessages;

        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            MessageItems.Add(_objectFactory.Create<LogMessageListItemViewModel>(message, Display.TimestampFormat));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var filtered = _messageStream.Search(_activeSession.Id, _searchQuery);
            MessageItems.Clear();
            foreach (var message in filtered)
            {
                MessageItems.Add(_objectFactory.Create<LogMessageListItemViewModel>(message, Display.TimestampFormat));
            }

            return;
        }

        // If no query, reload baseline (up to max).
        MessageItems.Clear();
        var max = _settingsService.Current.Display.MaxMessages;
        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            MessageItems.Add(_objectFactory.Create<LogMessageListItemViewModel>(message, Display.TimestampFormat));
        }
    }

    private void TrimMessages()
    {
        var max = _settingsService.Current.Display.MaxMessages;
        while (MessageItems.Count > max)
        {
            MessageItems.RemoveAt(0);
        }
    }

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(DisplaySettingsViewModel.TimestampFormat), StringComparison.Ordinal))
        {
            return;
        }

        foreach (var item in MessageItems)
        {
            item.UpdateTimestampFormat(Display.TimestampFormat);
        }
    }
}
