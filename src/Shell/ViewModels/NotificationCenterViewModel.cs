using System;
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
    private readonly IItemVmFactory<NotificationItemViewModel, NotificationItem> _itemFactory;
    private int _unreadCount;

    public NotificationCenterViewModel(
        ILocalizationService localization,
        NotificationService notificationService,
        IItemVmFactory<NotificationItemViewModel, NotificationItem> itemFactory)
        : base(localization)
    {
        _notificationService = notificationService;
        _itemFactory = itemFactory;
        _notificationService.NotificationAdded += OnNotificationAdded;

        Items = new ItemVmCollection<NotificationItemViewModel, NotificationItem>(_itemFactory);
        
        // 构造时同步加载数据
        _ = LoadAsync();
    }

    public ItemVmCollection<NotificationItemViewModel, NotificationItem> Items { get; }

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
                Items.Add(item);
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
            Items.Insert(0, item);
            UpdateUnreadCount();
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    private void UpdateUnreadCount()
    {
        UnreadCount = Items.Count(i => !i.IsRead);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notificationService.NotificationAdded -= OnNotificationAdded;
            Items.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class NotificationItemViewModel : LocalizedItemViewModelBase<NotificationItem>
{
    private NotificationItem? _item;
    private object[] _args = Array.Empty<object>();

    public NotificationItemViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    protected override void OnInit(NotificationItem item)
    {
        _item = item;
        _args = ParseArgs(item.MessageArgsJson);
        OnPropertyChanged(null);
    }

    public string Id => (_item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.")).Id;
    public DateTime CreatedAt => (_item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.")).CreatedAt.ToLocalTime();
    public NotificationLevel Level => (_item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.")).Level;

    public string Message
    {
        get
        {
            var item = _item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.");
            return Localization.GetString(item.MessageKey, _args);
        }
    }

    public bool IsRead
    {
        get => (_item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.")).IsRead;
        set
        {
            var item = _item ?? throw new InvalidOperationException("NotificationItemViewModel not initialized.");

            if (item.IsRead == value)
            {
                return;
            }

            item.IsRead = value;
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
