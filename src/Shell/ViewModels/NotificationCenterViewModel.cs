using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class NotificationCenterViewModel : BaseViewModel
{
    private readonly NotificationService _notificationService;
    private int _unreadCount;

    public NotificationCenterViewModel(
        ILocalizationService localization,
        NotificationService notificationService)
        : base(localization)
    {
        _notificationService = notificationService;
        _notificationService.NotificationAdded += OnNotificationAdded;
        
        // 构造时同步加载数据
        _ = LoadAsync();
    }

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
                Items.Add(new NotificationItemViewModel(item, Localization));
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

    public async Task DeleteAsync(string id)
    {
        await _notificationService.DeleteAsync(id);
        Dispatcher.UIThread.Post(() =>
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                Items.Remove(item);
                UpdateUnreadCount();
                OnPropertyChanged(nameof(HasNotifications));
                OnPropertyChanged(nameof(IsEmpty));
            }
        });
    }

    public async Task ClearAllAsync()
    {
        await _notificationService.ClearAllAsync();
        Dispatcher.UIThread.Post(() =>
        {
            Items.Clear();
            UpdateUnreadCount();
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    private void OnNotificationAdded(object? sender, NotificationItem item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Items.Insert(0, new NotificationItemViewModel(item, Localization));
            UpdateUnreadCount();
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    private void UpdateUnreadCount()
    {
        UnreadCount = Items.Count(i => !i.IsRead);
    }
}

public sealed class NotificationItemViewModel : BaseViewModel
{
    private readonly NotificationItem _item;
    private readonly object[] _args;

    public NotificationItemViewModel(NotificationItem item, ILocalizationService localization)
        : base(localization)
    {
        _item = item;
        _args = ParseArgs(item.MessageArgsJson);
    }

    public string Id => _item.Id;
    public DateTime CreatedAt => _item.CreatedAt.ToLocalTime();
    public NotificationLevel Level => _item.Level;

    public string Message
    {
        get => Localization.GetString(_item.MessageKey, _args);
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

}
