using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ComCross.Core.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Services;
using ComCross.Shell.Views;

namespace ComCross.Shell.ViewModels;

public class MainWindowViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;
    private readonly AppLogService _appLogService;
    private readonly Core.Application.IAppHost _appHost;
    private readonly IProgressDialogFactory _progressDialogFactory;
    
    // Business services
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private bool _isSettingsOpen;
    private bool _isNotificationsOpen;

    public LeftSidebarViewModel LeftSidebar { get; }
    public MessageStreamViewModel MessageStream { get; }
    public RightToolDockViewModel RightToolDock { get; }

    /// <summary>
    /// Workload tabs ViewModel (延迟加载)
    /// </summary>
    public WorkloadTabsViewModel WorkloadTabsViewModel { get; private set; } = null!;

    /// <summary>
    /// Bus adapter selector ViewModel
    /// </summary>
    public BusAdapterSelectorViewModel BusAdapterSelectorViewModel { get; private set; } = null!;

    /// <summary>
    /// Workspace coordinator
    /// </summary>
    public IWorkspaceCoordinator WorkspaceCoordinator => _workspaceCoordinator;

    // Back-compat: some legacy views still bind to MainWindowViewModel directly.
    public ObservableCollection<Session> Sessions => LeftSidebar.Sessions;
    public Session? ActiveSession
    {
        get => LeftSidebar.ActiveSession;
        set => LeftSidebar.ActiveSession = value;
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
        get => MessageStream.SearchQuery;
        set
        {
            MessageStream.SearchQuery = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected => RightToolDock.IsConnected;

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
        get => RightToolDock.SelectedToolTab;
        set
        {
            RightToolDock.SelectedToolTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSendTabActive));
            OnPropertyChanged(nameof(IsCommandsTabActive));
        }
    }

    public bool IsSendTabActive => RightToolDock.IsSendTabActive;
    public bool IsCommandsTabActive => RightToolDock.IsCommandsTabActive;

    public string Title => L["app.title"];

    public DisplaySettingsViewModel Display { get; }
    public SessionsViewModel SessionsVm { get; }

    /// <summary>
    /// Total received bytes across all sessions in current workload
    /// </summary>
    public long TotalRxBytes => _workspaceCoordinator.TotalRxBytes;

    /// <summary>
    /// Total transmitted bytes across all sessions in current workload
    /// </summary>
    public long TotalTxBytes => _workspaceCoordinator.TotalTxBytes;

    public SettingsViewModel Settings { get; }

    public NotificationCenterViewModel NotificationCenter { get; }

    public CommandCenterViewModel CommandCenter { get; }

    public PluginManagerViewModel PluginManager { get; }

    // Commands
    public ICommand ClearMessagesCommand { get; }
    public ICommand ExportMessagesCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand CloseNotificationsCommand { get; }

    public MainWindowViewModel(
        ILocalizationService localization,
        SettingsService settingsService,
        AppLogService appLogService,
        Core.Application.IAppHost appHost,
        IWorkspaceCoordinator workspaceCoordinator,
        WorkloadTabsViewModel workloadTabsViewModel,
        BusAdapterSelectorViewModel busAdapterSelectorViewModel,
        NotificationCenterViewModel notificationCenterViewModel,
        CommandCenterViewModel commandCenterViewModel,
        PluginManagerViewModel pluginManagerViewModel,
        SettingsViewModel settingsViewModel,
        DisplaySettingsViewModel displaySettingsViewModel,
        SessionsViewModel sessionsViewModel,
        LeftSidebarViewModel leftSidebar,
        MessageStreamViewModel messageStream,
        RightToolDockViewModel rightToolDock,
        IProgressDialogFactory progressDialogFactory)
        : base(localization)
    {
        _settingsService = settingsService;
        _appLogService = appLogService;
        _appHost = appHost;
        _workspaceCoordinator = workspaceCoordinator;
        _progressDialogFactory = progressDialogFactory;
        WorkloadTabsViewModel = workloadTabsViewModel;
        BusAdapterSelectorViewModel = busAdapterSelectorViewModel;

        NotificationCenter = notificationCenterViewModel;
        CommandCenter = commandCenterViewModel;
        PluginManager = pluginManagerViewModel;
        Settings = settingsViewModel;
        Display = displaySettingsViewModel;
        SessionsVm = sessionsViewModel;

        // Sub-viewmodels for MainWindow's 3 subviews (DI-scoped and shared).
        LeftSidebar = leftSidebar;
        MessageStream = messageStream;
        RightToolDock = rightToolDock;

        LeftSidebar.ActiveSessionChanged += (_, session) =>
        {
            MessageStream.SetActiveSession(session);
            RightToolDock.SetActiveSession(session);
            OnPropertyChanged(nameof(ActiveSession));
            OnPropertyChanged(nameof(IsConnected));
        };

        // 子 ViewModel 会通过自己的构造函数或订阅机制处理数据加载
        // 我们只需要在插件列表变化时同步适配器选择器
        PluginManager.PluginsReloaded += (_, _) =>
        {
            BusAdapterSelectorViewModel.UpdatePluginAdapters(PluginManager.GetAllCapabilityOptions());
        };

        // Initialize UI-only commands
        ClearMessagesCommand = new RelayCommand(() => RightToolDock.ClearMessages());
        ExportMessagesCommand = new AsyncRelayCommand(() => RightToolDock.ExportAsync());
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        CloseNotificationsCommand = new RelayCommand(() => IsNotificationsOpen = false);
        
        // 订阅语言变更事件（用于通知 Core/Plugins），UI 文本刷新由 BaseViewModel 统一处理。
        Localization.LanguageChanged += OnLanguageChanged;
        
        // Subscribe to statistics updates from coordinator
        _workspaceCoordinator.StatisticsUpdated += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(TotalRxBytes));
                OnPropertyChanged(nameof(TotalTxBytes));
            });
        };
        
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _appLogService.Initialize(_settingsService.Current.AppLogs);

            // UI 初始化：确保当前 Tab 状态正确
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsSendTabActive));
                OnPropertyChanged(nameof(IsCommandsTabActive));
            });

            // 获取初始插件能力列表（核心层已在 AppHost 加载完毕）
            BusAdapterSelectorViewModel.UpdatePluginAdapters(PluginManager.GetAllCapabilityOptions());

            // 加载工作区 UI 状态
            var state = await _workspaceCoordinator.LoadStateAsync();
            
            // 恢复 UI 状态 (Session/Tab 等)
            if (state.UiState?.ActiveSessionId != null)
            {
                LeftSidebar.SetPreferredActiveSessionId(state.UiState.ActiveSessionId);
            }

            RightToolDock.SetActiveSession(LeftSidebar.ActiveSession);

            // Workloads: ensure default workload and load tabs.
            await _workspaceCoordinator.EnsureDefaultWorkloadAsync();
            await WorkloadTabsViewModel.LoadWorkloadsAsync();

            _appLogService.Info("Application UI state initialized.");
        }
        catch (Exception ex)
        {
            _appLogService?.LogException(ex, "UI initialization error");
        }
    }

    // Back-compat helpers (used by some code-behind):
    public Task SendAsync(string message, bool hex, bool addCr, bool addLf)
        => RightToolDock.SendAsync(message, hex, addCr, addLf);

    public void ClearMessages() => RightToolDock.ClearMessages();

    public Task ExportAsync(string? filePath = null) => RightToolDock.ExportAsync(filePath);

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

    private async void OnLanguageChanged(object? sender, string cultureCode)
    {
        // 通知核心层同步外部插件语言 (由于 Core 不感知 UI，我们需要从 UI 入口点转发这个通知)
        await _appHost.NotifyLanguageChangedAsync(cultureCode);
        
        // 注意：其他子 ViewModel (NotificationCenter, CommandCenter) 应该自行订阅 
        // localization.LanguageChanged 事件来刷新自己的本地化文本，而不是由 MainWindowViewModel 强制驱动。
    }

    // PropertyChanged implementation inherited from BaseViewModel

    private void UpdateCommandStates()
    {
        (ClearMessagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportMessagesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task SaveWorkspaceStateAsync()
    {
        try
        {
            await _workspaceCoordinator.SaveCurrentStateAsync(Sessions, ActiveSession, Display.AutoScrollEnabled);
        }
        catch (Exception ex)
        {
            _appLogService.LogException(ex, "Failed to save workspace state");
        }
    }
    
    /// <summary>
    /// Cleanup with progress dialog - runs on background thread
    /// </summary>
    public async Task CleanupWithProgressAsync()
    {
        var progressDialog = _progressDialogFactory.CreateViewModel();
        var progressWindow = await _progressDialogFactory.ShowAsync(progressDialog);

        try
        {
            _appLogService.Info("Starting application cleanup with progress...");
            
            progressDialog.UpdateStatus(L["shutdown.disconnecting"]);

            // Save workspace state
            progressDialog.UpdateStatus(L["shutdown.savingState"]);
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
            
            // Dispose UI resources
            progressDialog.UpdateStatus(L["shutdown.cleaningUp"]);
            await Task.Run(() =>
            {
                MessageStream.SetActiveSession(null);
            });
            
            progressDialog.UpdateStatus(L["shutdown.complete"]);
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
