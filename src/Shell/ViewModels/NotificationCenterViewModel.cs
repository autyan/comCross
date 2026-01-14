using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class NotificationCenterViewModel : INotifyPropertyChanged
{
    private readonly NotificationService _notificationService;
    private readonly ILocalizationService _localization;
    private readonly LocalizedStringsViewModel _localizedStrings;
    private int _unreadCount;

    public NotificationCenterViewModel(
        NotificationService notificationService,
        ILocalizationService localization,
        LocalizedStringsViewModel localizedStrings)
    {
        _notificationService = notificationService;
        _localization = localization;
        _localizedStrings = localizedStrings;
        _notificationService.NotificationAdded += OnNotificationAdded;
    }

    public LocalizedStringsViewModel LocalizedStrings => _localizedStrings;

    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();

    public int UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (_unreadCount == value)
            {
                return;
            }

            _unreadCount = value;
            OnPropertyChanged();
        }
    }

    public bool HasNotifications => Items.Count > 0;
    public bool IsEmpty => !HasNotifications;

    public async Task LoadAsync()
    {
        var items = await _notificationService.GetRecentAsync();
        Dispatcher.UIThread.Post(() =>
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new NotificationItemViewModel(item, _localization));
            }

            UpdateUnreadCount();
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    public async Task MarkAllReadAsync()
    {
        await _notificationService.MarkAllReadAsync();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in Items)
            {
                item.IsRead = true;
            }

            UpdateUnreadCount();
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    public void RefreshLocalizedText()
    {
        foreach (var item in Items)
        {
            item.RefreshMessage(_localization);
        }

        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnNotificationAdded(object? sender, NotificationItem item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Items.Insert(0, new NotificationItemViewModel(item, _localization));
            UpdateUnreadCount();
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    private void UpdateUnreadCount()
    {
        UnreadCount = Items.Count(i => !i.IsRead);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class NotificationItemViewModel : INotifyPropertyChanged
{
    private readonly NotificationItem _item;
    private string _message;

    public NotificationItemViewModel(NotificationItem item, ILocalizationService localization)
    {
        _item = item;
        _message = FormatMessage(localization, item);
    }

    public string Id => _item.Id;
    public DateTime CreatedAt => _item.CreatedAt.ToLocalTime();
    public NotificationLevel Level => _item.Level;

    public string Message
    {
        get => _message;
        private set
        {
            if (_message == value)
            {
                return;
            }

            _message = value;
            OnPropertyChanged();
        }
    }

    public bool IsRead
    {
        get => _item.IsRead;
        set
        {
            if (_item.IsRead == value)
            {
                return;
            }

            _item.IsRead = value;
            OnPropertyChanged();
        }
    }

    public void RefreshMessage(ILocalizationService localization)
    {
        Message = FormatMessage(localization, _item);
    }

    private static string FormatMessage(ILocalizationService localization, NotificationItem item)
    {
        var args = ParseArgs(item.MessageArgsJson);
        return localization.GetString(item.MessageKey, args);
    }

    private static object[] ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<object>();
            }

            var args = new List<object>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                args.Add(ParseArg(element));
            }

            return args.ToArray();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static object ParseArg(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.ToString() ?? string.Empty
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
