using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ComCross.Adapters.Serial;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Shell.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EventBus _eventBus;
    private readonly MessageStreamService _messageStream;
    private readonly DeviceService _deviceService;
    private readonly SerialAdapter _serialAdapter;
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
    
    // Business services
    private readonly WorkspaceService _workspaceService;
    private readonly WorkloadService _workloadService;
    private readonly ExportService _exportService;
    private readonly Dictionary<string, IDisposable> _messageSubscriptions = new();
    private Session? _activeSession;
    private Device? _selectedDevice;
    private string _searchQuery = string.Empty;
    private bool _isConnected;
    private bool _isSettingsOpen;
    private bool _isNotificationsOpen;
    private ToolDockTab _selectedToolTab = ToolDockTab.Send;

    // Timer for periodic updates
    private readonly DispatcherTimer _statisticsUpdateTimer;
    private readonly object _statisticsLock = new object();
    private long _cachedTotalRxBytes;
    private long _cachedTotalTxBytes;

    public ObservableCollection<Session> Sessions { get; } = new();
    public ObservableCollection<Device> Devices { get; } = new();
    public ObservableCollection<LogMessage> Messages { get; } = new();

    /// <summary>
    /// Workload panel ViewModel
    /// </summary>
    public WorkloadPanelViewModel WorkloadPanelViewModel { get; private set; } = null!;

    /// <summary>
    /// Workload tabs ViewModel (延迟加载)
    /// </summary>
    public WorkloadTabsViewModel? WorkloadTabsViewModel { get; private set; }

    /// <summary>
    /// Bus adapter selector ViewModel
    /// </summary>
    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel { get; private set; } = null!;

    /// <summary>
    /// Workload service (exposed for initialization)
    /// </summary>
    public WorkloadService WorkloadService => _workloadService;

    public Device? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice != value)
            {
                _selectedDevice = value;
                OnPropertyChanged();
                (QuickConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (QuickConnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

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
                
                // Sync device selection and connection state
                if (_activeSession != null)
                {
                    var device = Devices.FirstOrDefault(d => d.Port == _activeSession.Port);
                    if (device != null && SelectedDevice != device)
                    {
                        SelectedDevice = device;
                    }
                    IsConnected = _activeSession.Status == SessionStatus.Connected;
                }
            }
        }
    }

    public async Task UpdateSessionNameAsync(string newName)
    {
        if (ActiveSession == null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var oldName = ActiveSession.Name;
        ActiveSession.Name = newName;
        OnPropertyChanged(nameof(ActiveSession));

        // Update command center
        CommandCenter.SetSession(ActiveSession.Id, newName);

        // Log the change
        _appLogService.Info($"Session name updated: '{oldName}' -> '{newName}'");
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
                UpdateCommandStates();
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
    public string MessageFontFamily => _settingsService.Current.Display.FontFamily;
    public int MessageFontSize => _settingsService.Current.Display.FontSize;

    /// <summary>
    /// Total received bytes across all sessions in current workload
    /// </summary>
    public long TotalRxBytes
    {
        get
        {
            lock (_statisticsLock)
            {
                return _cachedTotalRxBytes;
            }
        }
    }

    /// <summary>
    /// Total transmitted bytes across all sessions in current workload
    /// </summary>
    public long TotalTxBytes
    {
        get
        {
            lock (_statisticsLock)
            {
                return _cachedTotalTxBytes;
            }
        }
    }

    public ILocalizationService Localization => _localization;

    public LocalizedStringsViewModel LocalizedStrings { get; }

    public SettingsViewModel Settings { get; }

    public NotificationCenterViewModel NotificationCenter { get; }

    public CommandCenterViewModel CommandCenter { get; }

    public PluginManagerViewModel PluginManager { get; }

    // Commands
    public ICommand QuickConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearMessagesCommand { get; }
    public ICommand ExportMessagesCommand { get; }

    public MainWindowViewModel()
    {
        // Core infrastructure
        _eventBus = new EventBus();
        _messageStream = new MessageStreamService();
        
        // Configuration and database
        _configService = new ConfigService();
        _database = new AppDatabase();
        _settingsService = new SettingsService(_configService, _database);
        
        // Localization
        _localization = new LocalizationService();
        LocalizedStrings = new LocalizedStringsViewModel(_localization);
        
        // Notification system (depends on database and settings)
        _notificationService = new NotificationService(_database, _settingsService);
        
        // Device services (depends on notification)
        _serialAdapter = new SerialAdapter();
        _deviceService = new DeviceService(_serialAdapter, _eventBus, _messageStream, _notificationService);
        
        // Other services
        _appLogService = new AppLogService();
        _logStorageService = new LogStorageService(_messageStream, _settingsService, _notificationService, _database);
        _commandService = new CommandService(_settingsService);
        
        // Plugin system
        _pluginDiscoveryService = new PluginDiscoveryService();
        _pluginRuntimeService = new PluginRuntimeService();

        // Business services
        _workloadService = new WorkloadService(
            NullLogger<WorkloadService>.Instance, 
            _eventBus, 
            _configService,
            _localization);
        var migrationService = new WorkspaceMigrationService(
            NullLogger<WorkspaceMigrationService>.Instance);
        _workspaceService = new WorkspaceService(
            _deviceService, 
            _messageStream, 
            _logStorageService, 
            _notificationService, 
            _configService,
            _workloadService,
            migrationService);
        
        BusAdapterSelectorViewModel = new BusAdapterSelectorViewModel();
        WorkloadTabsViewModel = new WorkloadTabsViewModel(_workloadService, _eventBus, LocalizedStrings);
        
        // Initialize ViewModels (order matters due to dependencies)
        NotificationCenter = new NotificationCenterViewModel(
            _notificationService,
            _localization,
            LocalizedStrings);
        CommandCenter = new CommandCenterViewModel(
            _commandService,
            _settingsService,
            _notificationService,
            LocalizedStrings);
        PluginManager = new PluginManagerViewModel(
            _pluginDiscoveryService,
            _pluginRuntimeService,
            _settingsService,
            LocalizedStrings);
        Settings = new SettingsViewModel(
            _settingsService,
            _localization,
            LocalizedStrings,
            PluginManager);
        
        // Initialize statistics update timer (1 second interval)
        _statisticsUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statisticsUpdateTimer.Tick += OnStatisticsUpdateTick;
        _statisticsUpdateTimer.Start();
        
        // Subscribe to workload change events to update statistics immediately
        _eventBus.Subscribe<ActiveWorkloadChangedEvent>(e =>
        {
            UpdateStatistics();
        });
        
        // _ = InitializeAsync();
    }

    private void OnStatisticsUpdateTick(object? sender, EventArgs e)
    {
        UpdateStatistics();
    }

    /// <summary>
    /// Update cached statistics from all sessions (thread-safe)
    /// </summary>
    public void UpdateStatistics()
    {
        lock (_statisticsLock)
        {
            _cachedTotalRxBytes = Sessions.Sum(s => s.RxBytes);
            _cachedTotalTxBytes = Sessions.Sum(s => s.TxBytes);
        }
        
        // Notify UI on main thread
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(TotalRxBytes));
            OnPropertyChanged(nameof(TotalTxBytes));
        });
    }

    private async Task InitializeAsync()
    {
        try
        {
            Console.WriteLine("[MainWindowViewModel] Starting initialization...");
            
            await _database.InitializeAsync();
            Console.WriteLine("[MainWindowViewModel] Database initialized");
            
            await _settingsService.InitializeAsync();
            Console.WriteLine("[MainWindowViewModel] Settings initialized");
            
            _appLogService.Initialize(_settingsService.Current.AppLogs);
            _settingsService.SettingsChanged += OnSettingsChanged;
            
            // Initialize MessageBoxService with localization
            Shell.Services.MessageBoxService.Initialize(_localization);
            
            Dispatcher.UIThread.Post(() =>
            {
                Settings.ReloadFromSettings();
                Settings.ApplySystemLanguageIfNeeded();
                
                // Trigger initial property notifications for tool tab state
                OnPropertyChanged(nameof(IsSendTabActive));
                OnPropertyChanged(nameof(IsCommandsTabActive));
            });

            // Configure SerialAdapter with Linux scan settings
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _serialAdapter.ConfigureLinuxScan(_settingsService.Current.Connection.LinuxSerialScan);
            }

            // Load devices
            Console.WriteLine("[MainWindowViewModel] Loading devices...");
            var devices = await _deviceService.ListDevicesAsync();
            Console.WriteLine($"[MainWindowViewModel] Found {devices.Count} devices");
            
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            await NotificationCenter.LoadAsync();
            Console.WriteLine("[MainWindowViewModel] Notifications loaded");
            
            await PluginManager.LoadAsync();
            Console.WriteLine("[MainWindowViewModel] Plugins loaded");

            // Load workspace state (with automatic migration from v0.3 if needed)
            var state = await _workspaceService.LoadStateAsync();
            Console.WriteLine("[MainWindowViewModel] Workspace state loaded");
            
            // In v0.4, sessions are stored in Workloads
            // For now, keep legacy restoration logic for Sessions (if any exist from migration)
            // TODO v0.4: Implement proper Session persistence in SQLite with WorkloadId
#pragma warning disable CS0618 // Type or member is obsolete
            if (state.Sessions != null && state.Sessions.Count > 0)
            {
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
        }
#pragma warning restore CS0618

        // Restore UI state
        if (state.UiState?.ActiveSessionId != null)
        {
            ActiveSession = Sessions.FirstOrDefault(s => s.Id == state.UiState.ActiveSessionId);
        }

        CommandCenter.SetSession(ActiveSession?.Id, ActiveSession?.Name);
        _appLogService.Info("Application initialized.");
            
            Console.WriteLine("[MainWindowViewModel] Initialization complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindowViewModel] FATAL ERROR during initialization: {ex.Message}");
            Console.WriteLine($"[MainWindowViewModel] Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[MainWindowViewModel] Inner exception: {ex.InnerException.Message}");
            }
            _appLogService?.LogException(ex, "Fatal initialization error");
        }
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

            // Subscribe to messages (only if not already subscribed)
            if (!_messageSubscriptions.ContainsKey(sessionId))
            {
                var subscription = _messageStream.Subscribe(sessionId, message =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ActiveSession?.Id == sessionId)
                        {
                            Messages.Add(message);
                            TrimMessages();
                        }
                    });
                });
                _messageSubscriptions[sessionId] = subscription;
            }
        }
        catch (SerialPortAccessDeniedException ex)
        {
            _appLogService.LogException(ex, "Serial port access denied");
            await Shell.Services.MessageBoxService.ShowSerialPortAccessDeniedErrorAsync(port, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            _appLogService.LogException(ex, "Connect failed");
        }
    }

    public async Task QuickConnectAsync()
    {
        if (SelectedDevice == null)
        {
            await Shell.Services.MessageBoxService.ShowWarningAsync(
                LocalizedStrings.ConnectionErrorNoPortSelected,
                LocalizedStrings.ConnectionErrorNoPortSelectedMessage);
            return;
        }

        var port = SelectedDevice.Port;
        
        // If current active session is for this port, just reconnect it
        if (ActiveSession != null && ActiveSession.Port == port)
        {
            if (ActiveSession.Status == SessionStatus.Disconnected)
            {
                await ReconnectSessionAsync(ActiveSession);
            }
            // Already connected to this port, do nothing
            return;
        }
        
        // Check if there's an existing session for this port
        var existingSession = Sessions.FirstOrDefault(s => s.Port == port);
        if (existingSession != null)
        {
            var behavior = _settingsService.Current.Connection.ExistingSessionBehavior;
            
            switch (behavior)
            {
                case ConnectionBehavior.SwitchToExisting:
                    ActiveSession = existingSession;
                    if (existingSession.Status == SessionStatus.Disconnected)
                    {
                        // Reconnect
                        await ReconnectSessionAsync(existingSession);
                    }
                    return;
                    
                case ConnectionBehavior.PromptUser:
                    var switchToExisting = LocalizedStrings.ConnectionConfirmOk;
                    var createNew = LocalizedStrings.ConnectionConfirmCancel;
                    var cancel = _localization.GetString("messagebox.cancel");
                    
                    var choice = await Shell.Services.MessageBoxService.ShowCustomAsync(
                        LocalizedStrings.ConnectionConfirmExistingSessionTitle,
                        string.Format(_localization.GetString("connection.confirm.existingSession.message"), port),
                        Shell.Services.MessageBoxIcon.Question,
                        switchToExisting, createNew, cancel);
                    
                    if (choice == 0) // Switch to existing
                    {
                        ActiveSession = existingSession;
                        if (existingSession.Status == SessionStatus.Disconnected)
                        {
                            await ReconnectSessionAsync(existingSession);
                        }
                        return;
                    }
                    else if (choice == 1) // Create new
                    {
                        // Continue to create new session
                        break;
                    }
                    else // Cancel (choice == 2 or -1)
                    {
                        return;
                    }
                    
                case ConnectionBehavior.CreateNew:
                default:
                    // Continue to create new session
                    break;
            }
        }

        var settings = new SerialSettings { BaudRate = 115200 };
        var name = port;

        try
        {            var session = await _workspaceService.ConnectAsync(port, settings, name);
            Sessions.Add(session);
            ActiveSession = session;
            IsConnected = true;

            CommandCenter.SetSession(session.Id, session.Name);

            // Subscribe to messages
            _workspaceService.SubscribeToMessages(session.Id, message =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(message);
                    TrimMessages();
                });
            });

            // Save workspace state
            await SaveWorkspaceStateAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            _appLogService.LogException(ex, "Connect failed");
            await Shell.Services.MessageBoxService.ShowErrorAsync(
                LocalizedStrings.ConnectionErrorFailed,
                string.Format(_localization.GetString("connection.error.failedMessage"), port, ex.Message));
        }
    }

    public async Task DisconnectAsync()
    {
        if (ActiveSession == null) return;

        try
        {
            await _workspaceService.DisconnectAsync(ActiveSession.Id);
            ActiveSession.Status = SessionStatus.Disconnected;
            IsConnected = false;
            CommandCenter.SetSession(null, null);

            // Save workspace state
            await SaveWorkspaceStateAsync();
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Disconnect failed");
        }
    }

    public async Task RefreshDevicesAsync()
    {
        try
        {
            // Reconfigure adapter if on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _serialAdapter.ConfigureLinuxScan(_settingsService.Current.Connection.LinuxSerialScan);
            }

            var devices = await _deviceService.ListDevicesAsync();
            
            // Remember current selection
            var currentPort = SelectedDevice?.Port;
            
            Dispatcher.UIThread.Post(() =>
            {
                Devices.Clear();
                foreach (var device in devices)
                {
                    Devices.Add(device);
                }
                
                // Restore selection if the port still exists
                if (currentPort != null)
                {
                    SelectedDevice = Devices.FirstOrDefault(d => d.Port == currentPort);
                }
            });
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Refresh devices failed");
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            await _workspaceService.DeleteSessionAsync(sessionId);
            
            var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                Sessions.Remove(session);
                if (ActiveSession?.Id == sessionId)
                {
                    ActiveSession = null;
                    IsConnected = false;
                }
            }
            
            // Save workspace state
            await SaveWorkspaceStateAsync();
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Delete session failed");
        }
    }

    private async Task ReconnectSessionAsync(Session oldSession)
    {
        try
        {
            var newSession = await _workspaceService.ConnectAsync(oldSession.Port, oldSession.Settings, oldSession.Name);
            
            // Replace old session with new connected session
            var index = Sessions.IndexOf(oldSession);
            if (index >= 0)
            {
                Sessions[index] = newSession;
                ActiveSession = newSession;
                IsConnected = true;
            }
            
            // Subscribe to messages (only if not already subscribed)
            if (!_messageSubscriptions.ContainsKey(newSession.Id))
            {
                var subscription = _workspaceService.SubscribeToMessages(newSession.Id, message =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ActiveSession?.Id == newSession.Id)
                        {
                            Messages.Add(message);
                            TrimMessages();
                        }
                    });
                });
                _messageSubscriptions[newSession.Id] = subscription;
            }
            
            await SaveWorkspaceStateAsync();
        }
        catch (SerialPortAccessDeniedException ex)
        {
            _appLogService.LogException(ex, "Serial port access denied");
            await Shell.Services.MessageBoxService.ShowSerialPortAccessDeniedErrorAsync(oldSession.Port, ex);
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Reconnect failed");
            await Shell.Services.MessageBoxService.ShowErrorAsync(
                LocalizedStrings.ConnectionErrorFailed,
                string.Format(_localization.GetString("connection.error.failedMessage"), oldSession.Port, ex.Message));
        }
    }

    public async Task SendAsync(string message, bool hex, bool addCr, bool addLf)
    {
        if (ActiveSession == null || !IsConnected) return;

        try
        {
            var format = hex ? MessageFormat.Hex : MessageFormat.Text;
            await _workspaceService.SendMessageAsync(ActiveSession.Id, message, format, addCr, addLf);
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
            _workspaceService.ClearMessages(ActiveSession.Id);
            Messages.Clear();
        }
    }

    public async Task ExportAsync(string? filePath = null)
    {
        if (ActiveSession == null) return;

        try
        {
            await _exportService.ExportAsync(ActiveSession, SearchQuery, filePath);
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Export failed");
        }
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
        if (ActiveSession == null || !IsConnected) return;

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

            await _workspaceService.SendDataAsync(ActiveSession.Id, data);
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
    
    private async void OnLinuxScanSettingsChanged(object? sender, EventArgs e)
    {
        // Linux scan settings changed, automatically refresh device list
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await RefreshDevicesAsync();
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _appLogService.Update(settings.AppLogs);
        OnPropertyChanged(nameof(TimestampFormat));
        OnPropertyChanged(nameof(AutoScrollEnabled));
        OnPropertyChanged(nameof(MessageFontFamily));
        OnPropertyChanged(nameof(MessageFontSize));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateCommandStates()
    {
        (DisconnectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearMessagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportMessagesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SaveWorkspaceStateAsync()
    {
        try
        {
            await _workspaceService.SaveCurrentStateAsync(Sessions, ActiveSession, AutoScrollEnabled);
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Failed to save workspace state");
        }
    }
    
    /// <summary>
    /// Cleanup resources before application exit
    /// </summary>
    public void Cleanup()
    {
        try
        {
            _appLogService.Info("Starting application cleanup...");
            
            // Disconnect all active sessions with longer timeout
            foreach (var session in Sessions.Where(s => s.Status == SessionStatus.Connected).ToList())
            {
                try
                {
                    _appLogService.Info($"Disconnecting session: {session.Name} ({session.Port})");
                    var disconnectTask = _deviceService.DisconnectAsync(session.Id);
                    if (!disconnectTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        _appLogService.Warn($"Disconnect timeout for session {session.Id}, forcing close");
                    }
                }
                catch (Exception ex)
                {
                    _appLogService.Warn($"Error disconnecting session {session.Id}: {ex.Message}");
                }
            }
            
            // Dispose all subscriptions
            foreach (var subscription in _messageSubscriptions.Values)
            {
                try
                {
                    subscription?.Dispose();
                }
                catch { }
            }
            _messageSubscriptions.Clear();
            
            // Dispose services
            _deviceService?.Dispose();
            
            _appLogService.Info("Application cleanup completed.");
        }
        catch (Exception ex)
        {
            _appLogService.Error($"Error during cleanup: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Cleanup with progress dialog - runs on background thread
    /// </summary>
    public async Task CleanupWithProgressAsync()
    {
        var progressDialog = new ProgressDialogViewModel(_localization);
        
        // Show progress dialog on UI thread
        var dialogTask = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new Window
            {
                Title = _localization.GetString("shutdown.title"),
                Width = 400,
                Height = 200,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Border
                {
                    Padding = new Avalonia.Thickness(20),
                    Child = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = _localization.GetString("shutdown.message"),
                                FontSize = 14,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = progressDialog.CurrentStatus,
                                FontSize = 12,
                                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#87909B")),
                                [!TextBlock.TextProperty] = new Avalonia.Data.Binding("CurrentStatus") { Source = progressDialog }
                            },
                            new ProgressBar
                            {
                                IsIndeterminate = true,
                                Height = 4
                            }
                        }
                    }
                }
            };

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                dialog.Show(desktop.MainWindow);
            }
            else
            {
                dialog.Show();
            }

            return dialog;
        });

        Window? progressWindow = await dialogTask;

        try
        {
            _appLogService.Info("Starting application cleanup with progress...");
            
            var connectedSessions = Sessions.Where(s => s.Status == SessionStatus.Connected).ToList();
            
            // Disconnect all active sessions on background thread
            foreach (var session in connectedSessions)
            {
                try
                {
                    progressDialog.UpdateStatus(string.Format(
                        _localization.GetString("shutdown.disconnecting"),
                        session.Name));
                    
                    _appLogService.Info($"Disconnecting session: {session.Name} ({session.Port})");
                    
                    // Run disconnect on background thread with timeout
                    await Task.Run(async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        try
                        {
                            await _deviceService.DisconnectAsync(session.Id);
                        }
                        catch (OperationCanceledException)
                        {
                            _appLogService.Warn($"Disconnect timeout for session {session.Id}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _appLogService.Warn($"Error disconnecting session {session.Id}: {ex.Message}");
                }
                
                // Small delay to ensure cleanup completes
                await Task.Delay(100);
            }
            
            // Save workspace state
            progressDialog.UpdateStatus(_localization.GetString("shutdown.savingState"));
            await Task.Run(async () =>
            {
                try
                {
                    await SaveWorkspaceStateAsync();
                }
                catch (Exception ex)
                {
                    _appLogService.Warn($"Error saving workspace state: {ex.Message}");
                }
            });
            
            // Dispose resources
            progressDialog.UpdateStatus(_localization.GetString("shutdown.cleaningUp"));
            await Task.Run(() =>
            {
                // Dispose all subscriptions
                foreach (var subscription in _messageSubscriptions.Values)
                {
                    try
                    {
                        subscription?.Dispose();
                    }
                    catch { }
                }
                _messageSubscriptions.Clear();
                
                // Dispose services
                _deviceService?.Dispose();
            });
            
            progressDialog.UpdateStatus(_localization.GetString("shutdown.complete"));
            await Task.Delay(300); // Brief delay to show completion
            
            _appLogService.Info("Application cleanup completed.");
        }
        catch (Exception ex)
        {
            _appLogService.Error($"Error during cleanup: {ex.Message}", ex);
        }
        finally
        {
            // Close progress dialog on UI thread
            if (progressWindow != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            }
        }
    }
}

/// <summary>
/// Progress dialog view model for cleanup
/// </summary>
internal class ProgressDialogViewModel : INotifyPropertyChanged
{
    private string _currentStatus = string.Empty;
    private readonly ILocalizationService _localization;

    public ProgressDialogViewModel(ILocalizationService localization)
    {
        _localization = localization;
        _currentStatus = localization.GetString("shutdown.cleaningUp");
    }

    public string CurrentStatus
    {
        get => _currentStatus;
        private set
        {
            if (_currentStatus != value)
            {
                _currentStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStatus)));
            }
        }
    }

    public void UpdateStatus(string status)
    {
        CurrentStatus = status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum ToolDockTab
{
    Send,
    Commands
}
