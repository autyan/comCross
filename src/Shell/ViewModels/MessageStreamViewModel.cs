using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class MessageStreamViewModel : BaseViewModel
{
    private readonly IMessageStreamService _messageStream;
    private readonly SettingsService _settingsService;

    private IDisposable? _messageSubscription;

    private Session? _activeSession;
    private string _searchQuery = string.Empty;

    public MessageStreamViewModel(
        ILocalizationService localization,
        IMessageStreamService messageStream,
        SettingsService settingsService,
        DisplaySettingsViewModel display)
        : base(localization)
    {
        _messageStream = messageStream;
        _settingsService = settingsService;
        Display = display;
    }

    public DisplaySettingsViewModel Display { get; }

    public ObservableCollection<LogMessage> Messages { get; } = new();

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

                Messages.Add(message);
                TrimMessages();
            });
        });
    }

    public void ClearView()
    {
        Messages.Clear();
    }

    private void LoadMessages()
    {
        Messages.Clear();

        if (_activeSession?.Id is not { Length: > 0 })
        {
            return;
        }

        var max = _settingsService.Current.Display.MaxMessages;

        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            Messages.Add(message);
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
            Messages.Clear();
            foreach (var message in filtered)
            {
                Messages.Add(message);
            }

            return;
        }

        // If no query, reload baseline (up to max).
        Messages.Clear();
        var max = _settingsService.Current.Display.MaxMessages;
        var messages = _messageStream.GetMessages(_activeSession.Id, 0, max);
        foreach (var message in messages)
        {
            Messages.Add(message);
        }
    }

    private void TrimMessages()
    {
        var max = _settingsService.Current.Display.MaxMessages;
        while (Messages.Count > max)
        {
            Messages.RemoveAt(0);
        }
    }
}
