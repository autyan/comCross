using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComCross.Adapters.Serial;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EventBus _eventBus;
    private readonly MessageStreamService _messageStream;
    private readonly DeviceService _deviceService;
    private readonly ConfigService _configService;
    private readonly AppDatabase _database;
    private readonly SettingsService _settingsService;
    private readonly AppLogService _appLogService;
    private readonly NotificationService _notificationService;
    private readonly LogStorageService _logStorageService;
    private readonly CommandService _commandService;
    private readonly PluginDiscoveryService _pluginDiscoveryService;
    private readonly PluginRuntimeService _pluginRuntimeService;
    private readonly ILocalizationService _localization;
    private Session? _activeSession;
    private string _searchQuery = string.Empty;
    private bool _isConnected;
    private bool _isSettingsOpen;
    private bool _isNotificationsOpen;
    private ToolDockTab _selectedToolTab = ToolDockTab.Send;

    public ObservableCollection<Session> Sessions { get; } = new();
    public ObservableCollection<Device> Devices { get; } = new();
    public ObservableCollection<LogMessage> Messages { get; } = new();

    public Session? ActiveSession
    {
        get => _activeSession;
        set
        {
            if (_activeSession != value)
            {
                _activeSession = value;
                OnPropertyChanged();
                LoadMessages();
                CommandCenter.SetSession(_activeSession?.Id, _activeSession?.Name);
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterMessages();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (_isSettingsOpen != value)
            {
                _isSettingsOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsNotificationsOpen
    {
        get => _isNotificationsOpen;
        set
        {
            if (_isNotificationsOpen != value)
            {
                _isNotificationsOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public ToolDockTab SelectedToolTab
    {
        get => _selectedToolTab;
        set
        {
            if (_selectedToolTab == value)
            {
                return;
            }

            _selectedToolTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSendTabActive));
            OnPropertyChanged(nameof(IsCommandsTabActive));
        }
    }

    public bool IsSendTabActive => _selectedToolTab == ToolDockTab.Send;
    public bool IsCommandsTabActive => _selectedToolTab == ToolDockTab.Commands;

    public string Title => _localization.GetString("app.title");
    public string TimestampFormat => _settingsService.Current.Display.TimestampFormat;
    public bool AutoScrollEnabled => _settingsService.Current.Display.AutoScroll;

    public ILocalizationService Localization => _localization;

    public LocalizedStringsViewModel LocalizedStrings { get; }

    public SettingsViewModel Settings { get; }

    public NotificationCenterViewModel NotificationCenter { get; }

    public CommandCenterViewModel CommandCenter { get; }

    public PluginManagerViewModel PluginManager { get; }

    public MainWindowViewModel()
    {
        _eventBus = new EventBus();
        _messageStream = new MessageStreamService();
        var adapter = new SerialAdapter();
        _deviceService = new DeviceService(adapter, _eventBus, _messageStream);
        _configService = new ConfigService();
        _database = new AppDatabase();
        _settingsService = new SettingsService(_configService, _database);
        _appLogService = new AppLogService();
        _localization = new LocalizationService();
        LocalizedStrings = new LocalizedStringsViewModel(_localization);
        _notificationService = new NotificationService(_database, _settingsService);
        _logStorageService = new LogStorageService(_messageStream, _settingsService, _notificationService, _database);
        _commandService = new CommandService(_settingsService);
        _pluginDiscoveryService = new PluginDiscoveryService();
        _pluginRuntimeService = new PluginRuntimeService();

        PluginManager = new PluginManagerViewModel(
            _pluginDiscoveryService,
            _pluginRuntimeService,
            _settingsService,
            LocalizedStrings);
        Settings = new SettingsViewModel(_settingsService, _localization, LocalizedStrings, PluginManager);
        Settings.LanguageChanged += OnLanguageChanged;
        NotificationCenter = new NotificationCenterViewModel(_notificationService, _localization, LocalizedStrings);
        CommandCenter = new CommandCenterViewModel(
            _commandService,
            _settingsService,
            _notificationService,
            LocalizedStrings);
        CommandCenter.SendRequested += SendCommandAsync;

        _eventBus.Subscribe<DeviceDisconnectedEvent>(OnDeviceDisconnected);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await _settingsService.InitializeAsync();
        _appLogService.Initialize(_settingsService.Current.AppLogs);
        _settingsService.SettingsChanged += OnSettingsChanged;
        Dispatcher.UIThread.Post(() =>
        {
            Settings.ReloadFromSettings();
            Settings.ApplySystemLanguageIfNeeded();
        });

        // Load devices
        var devices = await _deviceService.ListDevicesAsync();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        await NotificationCenter.LoadAsync();
        await PluginManager.LoadAsync();

        // Load workspace state
        var state = await _configService.LoadWorkspaceStateAsync();
        if (state != null)
        {
            // Restore sessions (but don't auto-connect)
            foreach (var sessionState in state.Sessions)
            {
                var session = new Session
                {
                    Id = sessionState.Id,
                    Name = sessionState.Name,
                    Port = sessionState.Port,
                    BaudRate = sessionState.Settings.BaudRate,
                    Status = SessionStatus.Disconnected,
                    Settings = sessionState.Settings
                };
                Sessions.Add(session);
            }

            // Restore UI state
            if (state.UiState?.ActiveSessionId != null)
            {
                ActiveSession = Sessions.FirstOrDefault(s => s.Id == state.UiState.ActiveSessionId);
            }
        }

        CommandCenter.SetSession(ActiveSession?.Id, ActiveSession?.Name);
        _appLogService.Info("Application initialized.");
    }

    public async Task ConnectAsync(string port, int baudRate, string name)
    {
        try
        {
            var sessionId = $"session-{Guid.NewGuid()}";
            var settings = new SerialSettings { BaudRate = baudRate };

            var session = await _deviceService.ConnectAsync(sessionId, port, name, settings);
            Sessions.Add(session);
            ActiveSession = session;
            IsConnected = true;

            CommandCenter.SetSession(session.Id, session.Name);
            _logStorageService.StartSession(session);

            // Subscribe to messages
            _messageStream.Subscribe(sessionId, message =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(message);
                    TrimMessages();
                });
            });
        }
        catch (Exception ex)
        {
            // Handle error
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            _appLogService.LogException(ex, "Connect failed");
        }
    }

    public async Task DisconnectAsync()
    {
        if (ActiveSession != null)
        {
            try
            {
                await _deviceService.DisconnectAsync(ActiveSession.Id);
                await _logStorageService.StopSessionAsync(ActiveSession.Id);
            ActiveSession.Status = SessionStatus.Disconnected;
            IsConnected = false;
            CommandCenter.SetSession(null, null);
        }
            catch (Exception ex)
            {
                _appLogService.LogException(ex, "Disconnect failed");
            }
        }
    }

    public async Task SendAsync(string message, bool hex, bool addCr, bool addLf)
    {
        if (ActiveSession == null || !IsConnected) return;

        try
        {
            var data = hex
                ? Convert.FromHexString(message.Replace(" ", ""))
                : System.Text.Encoding.UTF8.GetBytes(message);

            if (addCr || addLf)
            {
                var suffix = (addCr ? "\r" : "") + (addLf ? "\n" : "");
                var suffixBytes = System.Text.Encoding.UTF8.GetBytes(suffix);
                data = data.Concat(suffixBytes).ToArray();
            }

            await _deviceService.SendAsync(ActiveSession.Id, data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Send failed: {ex.Message}");
            _appLogService.LogException(ex, "Send failed");
        }
    }

    public void ClearMessages()
    {
        if (ActiveSession != null)
        {
            _messageStream.Clear(ActiveSession.Id);
            Messages.Clear();
        }
    }

    public async Task ExportAsync(string? filePath = null)
    {
        if (ActiveSession == null)
        {
            return;
        }

        var format = ResolveExportFormat(filePath);
        var directory = ResolveExportDirectory(filePath);
        Directory.CreateDirectory(directory);

        var safeName = SanitizeFileName(ActiveSession.Name);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var targetPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(directory, $"{safeName}_{timestamp}.{format}")
            : filePath;

        var source = string.IsNullOrWhiteSpace(SearchQuery)
            ? _messageStream.GetMessages(ActiveSession.Id, 0, int.MaxValue)
            : _messageStream.Search(ActiveSession.Id, SearchQuery);
        source = ApplyExportRange(source);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(source, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(targetPath, json);
        }
        else
        {
            await using var writer = new StreamWriter(targetPath);
            foreach (var message in source)
            {
                await writer.WriteLineAsync($"{message.Timestamp:O}\t{message.Level}\t{message.Source}\t{message.Content}");
            }
        }

        await _notificationService.AddAsync(
            NotificationCategory.Export,
            NotificationLevel.Info,
            "notification.export.completed",
            new object[] { targetPath });
    }

    private IReadOnlyList<LogMessage> ApplyExportRange(IReadOnlyList<LogMessage> source)
    {
        var settings = _settingsService.Current.Export;
        if (settings.RangeMode != ExportRangeMode.Latest || settings.RangeCount <= 0)
        {
            return source;
        }

        if (source.Count <= settings.RangeCount)
        {
            return source;
        }

        return source.Skip(source.Count - settings.RangeCount).ToList();
    }

    private string ResolveExportDirectory(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _settingsService.Current.Export.DefaultDirectory = directory;
                _ = _settingsService.SaveAsync();
                return directory;
            }
        }

        var directorySetting = _settingsService.Current.Export.DefaultDirectory;
        if (!string.IsNullOrWhiteSpace(directorySetting))
        {
            return directorySetting;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross",
            "exports");
        _settingsService.Current.Export.DefaultDirectory = fallback;
        _ = _settingsService.SaveAsync();
        return fallback;
    }

    private string ResolveExportFormat(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var extension = Path.GetExtension(filePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.TrimStart('.');
            }
        }

        return _settingsService.Current.Export.DefaultFormat;
    }

    public void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        if (IsSettingsOpen)
        {
            IsNotificationsOpen = false;
        }
    }

    public void ToggleNotifications()
    {
        IsNotificationsOpen = !IsNotificationsOpen;
        if (IsNotificationsOpen)
        {
            IsSettingsOpen = false;
        }
    }

    private void LoadMessages()
    {
        Messages.Clear();
        if (ActiveSession != null)
        {
            var max = _settingsService.Current.Display.MaxMessages;
            var messages = _messageStream.GetMessages(ActiveSession.Id, 0, max);
            foreach (var message in messages)
            {
                Messages.Add(message);
            }
        }
    }

    private async Task SendCommandAsync(CommandDefinition command)
    {
        if (ActiveSession == null || !IsConnected)
        {
            return;
        }

        try
        {
            byte[] data;
            if (command.Type == CommandPayloadType.Hex)
            {
                data = Convert.FromHexString(command.Payload.Replace(" ", ""));
            }
            else
            {
                var encoding = GetEncoding(command.Encoding);
                data = encoding.GetBytes(command.Payload);
            }

            if (command.AppendCr || command.AppendLf)
            {
                var suffix = (command.AppendCr ? "\r" : "") + (command.AppendLf ? "\n" : "");
                var suffixBytes = System.Text.Encoding.UTF8.GetBytes(suffix);
                data = data.Concat(suffixBytes).ToArray();
            }

            await _deviceService.SendAsync(ActiveSession.Id, data);
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Command send failed");
        }
    }

    private static System.Text.Encoding GetEncoding(string name)
    {
        try
        {
            return System.Text.Encoding.GetEncoding(name);
        }
        catch
        {
            return System.Text.Encoding.UTF8;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(ch, '_');
        }

        return name;
    }

    private void FilterMessages()
    {
        Messages.Clear();
        if (ActiveSession != null && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            var filtered = _messageStream.Search(ActiveSession.Id, SearchQuery);
            foreach (var message in filtered)
            {
                Messages.Add(message);
            }
        }
        else
        {
            LoadMessages();
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

    private void OnDeviceDisconnected(DeviceDisconnectedEvent @event)
    {
        var reason = @event.Reason ?? _localization.GetString("notification.connection.unknownReason");
        _ = _notificationService.AddAsync(
            NotificationCategory.Connection,
            NotificationLevel.Warning,
            "notification.connection.disconnected",
            new object[]
            {
                @event.Port,
                reason
            });
    }

    private void OnLanguageChanged(object? sender, string cultureCode)
    {
        OnPropertyChanged(nameof(Title));
        NotificationCenter.RefreshLocalizedText();
        CommandCenter.RefreshLocalizedOptions();
        PluginManager.RefreshLocalizedText();
        PluginManager.NotifyLanguageChanged(cultureCode, (runtime, ex, restarted) =>
        {
            var message = restarted
                ? $"Plugin '{runtime.Info.Manifest.Id}' notification failed; plugin restarted."
                : $"Plugin '{runtime.Info.Manifest.Id}' notification failed; restart failed.";
            _appLogService.Error(message, ex);
        });
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _appLogService.Update(settings.AppLogs);
        OnPropertyChanged(nameof(TimestampFormat));
        OnPropertyChanged(nameof(AutoScrollEnabled));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum ToolDockTab
{
    Send,
    Commands
}
