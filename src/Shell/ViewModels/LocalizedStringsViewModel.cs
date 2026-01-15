using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// View model for localized strings
/// </summary>
public class LocalizedStringsViewModel : INotifyPropertyChanged
{
    private readonly ILocalizationService _localization;

    public LocalizedStringsViewModel(ILocalizationService localization)
    {
        _localization = localization;
    }

    // Main Window
    public string AppTitle => _localization.GetString("app.title");
    public string MenuConnect => _localization.GetString("menu.connect");
    public string MenuDisconnect => _localization.GetString("menu.disconnect");
    public string MenuClear => _localization.GetString("menu.clear");
    public string MenuExport => _localization.GetString("menu.export");

    // Connect Dialog
    public string DialogConnectTitle => _localization.GetString("dialog.connect.title");
    public string DialogConnectPort => _localization.GetString("dialog.connect.port");
    public string DialogConnectBaudRate => _localization.GetString("dialog.connect.baudrate");
    public string DialogConnectSessionName => _localization.GetString("dialog.connect.sessionname");
    public string DialogConnectSessionNamePlaceholder => _localization.GetString("dialog.connect.sessionname.placeholder");
    public string DialogConnectCancel => _localization.GetString("dialog.connect.cancel");
    public string DialogConnectConnect => _localization.GetString("dialog.connect.connect");

    // Sidebar
    public string SidebarDevices => _localization.GetString("sidebar.devices");
    public string SidebarSessions => _localization.GetString("sidebar.sessions");
    public string SidebarSelectPort => _localization.GetString("sidebar.selectPort");
    public string SidebarRefreshPorts => _localization.GetString("sidebar.refreshPorts");
    public string SidebarQuickConnect => _localization.GetString("sidebar.quickConnect");
    public string SidebarBaudRate => _localization.GetString("sidebar.baudRate");
    public string SidebarDataBits => _localization.GetString("sidebar.dataBits");
    public string SidebarParity => _localization.GetString("sidebar.parity");
    public string SidebarStopBits => _localization.GetString("sidebar.stopBits");
    public string SidebarParityNone => _localization.GetString("sidebar.parity.none");
    public string SidebarParityOdd => _localization.GetString("sidebar.parity.odd");
    public string SidebarParityEven => _localization.GetString("sidebar.parity.even");

    // Message Stream
    public string StreamSearchPlaceholder => _localization.GetString("stream.search.placeholder");
    public string StreamMetricsRx => _localization.GetString("stream.metrics.rx");
    public string StreamMetricsTx => _localization.GetString("stream.metrics.tx");
    public string StreamMetricsLines => _localization.GetString("stream.metrics.lines");

    // Tool Dock
    public string ToolSend => _localization.GetString("tool.send");
    public string ToolFilter => _localization.GetString("tool.filter");
    public string ToolHighlight => _localization.GetString("tool.highlight");
    public string ToolExport => _localization.GetString("tool.export");
    public string ToolSendQuickCommands => _localization.GetString("tool.send.quickcommands");
    public string ToolSendMessage => _localization.GetString("tool.send.message");
    public string ToolSendMessagePlaceholder => _localization.GetString("tool.send.message.placeholder");
    public string ToolSendHexMode => _localization.GetString("tool.send.hexmode");
    public string ToolSendAddCr => _localization.GetString("tool.send.addcr");
    public string ToolSendAddLf => _localization.GetString("tool.send.addlf");
    public string ToolSendButton => _localization.GetString("tool.send.button");
    public string ToolSendCmdStatus => _localization.GetString("tool.send.cmd.status");
    public string ToolSendCmdReset => _localization.GetString("tool.send.cmd.reset");
    public string ToolSendCmdGetConfig => _localization.GetString("tool.send.cmd.getconfig");
    public string ToolCommands => _localization.GetString("tool.commands");
    public string ToolCommandsEmpty => _localization.GetString("tool.commands.empty");
    public string ToolCommandsSend => _localization.GetString("tool.commands.send");
    public string ToolCommandsAdd => _localization.GetString("tool.commands.add");
    public string ToolCommandsSave => _localization.GetString("tool.commands.save");
    public string ToolCommandsDelete => _localization.GetString("tool.commands.delete");
    public string ToolCommandsImport => _localization.GetString("tool.commands.import");
    public string ToolCommandsExport => _localization.GetString("tool.commands.export");
    public string ToolCommandsName => _localization.GetString("tool.commands.name");
    public string ToolCommandsPayload => _localization.GetString("tool.commands.payload");
    public string ToolCommandsType => _localization.GetString("tool.commands.type");
    public string ToolCommandsEncoding => _localization.GetString("tool.commands.encoding");
    public string ToolCommandsGroup => _localization.GetString("tool.commands.group");
    public string ToolCommandsScope => _localization.GetString("tool.commands.scope");
    public string ToolCommandsAppendCr => _localization.GetString("tool.commands.appendCr");
    public string ToolCommandsAppendLf => _localization.GetString("tool.commands.appendLf");
    public string ToolCommandsHotkey => _localization.GetString("tool.commands.hotkey");
    public string ToolCommandsSortOrder => _localization.GetString("tool.commands.sortOrder");
    public string ToolCommandsScopeGlobal => _localization.GetString("tool.commands.scope.global");
    public string ToolCommandsScopeSession => _localization.GetString("tool.commands.scope.session");
    public string ToolCommandsTypeText => _localization.GetString("tool.commands.type.text");
    public string ToolCommandsTypeHex => _localization.GetString("tool.commands.type.hex");

    // Status Bar
    public string StatusReady => _localization.GetString("status.ready");

    // Settings
    public string SettingsTitle => _localization.GetString("settings.title");
    public string SettingsSectionGeneral => _localization.GetString("settings.section.general");
    public string SettingsSectionLogs => _localization.GetString("settings.section.logs");
    public string SettingsSectionAppLogs => _localization.GetString("settings.section.appLogs");
    public string SettingsSectionNotifications => _localization.GetString("settings.section.notifications");
    public string SettingsSectionConnection => _localization.GetString("settings.section.connection");
    public string SettingsSectionDisplay => _localization.GetString("settings.section.display");
    public string SettingsSectionExport => _localization.GetString("settings.section.export");
    public string SettingsSectionPlugins => _localization.GetString("settings.section.plugins");
    public string SettingsLanguage => _localization.GetString("settings.language");
    public string SettingsFollowSystemLanguage => _localization.GetString("settings.followSystemLanguage");
    public string SettingsLogsAutoSave => _localization.GetString("settings.logs.autosave");
    public string SettingsLogsDirectory => _localization.GetString("settings.logs.directory");
    public string SettingsLogsMaxFileSize => _localization.GetString("settings.logs.maxFileSize");
    public string SettingsLogsMaxTotalSize => _localization.GetString("settings.logs.maxTotalSize");
    public string SettingsLogsAutoDelete => _localization.GetString("settings.logs.autoDelete");
    public string SettingsLogsAutoDeleteRuleTip => _localization.GetString("settings.logs.autoDeleteRuleTip");
    public string SettingsAppLogsEnabled => _localization.GetString("settings.appLogs.enabled");
    public string SettingsAppLogsDirectory => _localization.GetString("settings.appLogs.directory");
    public string SettingsAppLogsFormat => _localization.GetString("settings.appLogs.format");
    public string SettingsAppLogsMinLevel => _localization.GetString("settings.appLogs.minLevel");
    public string SettingsPluginsEnabled => _localization.GetString("settings.plugins.enabled");
    public string SettingsPluginsName => _localization.GetString("settings.plugins.name");
    public string SettingsPluginsPermissions => _localization.GetString("settings.plugins.permissions");
    public string SettingsPluginsPath => _localization.GetString("settings.plugins.path");
    public string SettingsPluginsStatusLoaded => _localization.GetString("settings.plugins.status.loaded");
    public string SettingsPluginsStatusDisabled => _localization.GetString("settings.plugins.status.disabled");
    public string SettingsPluginsStatusFailed => _localization.GetString("settings.plugins.status.failed");
    public string SettingsNotificationsStorage => _localization.GetString("settings.notifications.storage");
    public string SettingsNotificationsConnection => _localization.GetString("settings.notifications.connection");
    public string SettingsNotificationsExport => _localization.GetString("settings.notifications.export");
    public string SettingsNotificationsRetentionDays => _localization.GetString("settings.notifications.retentionDays");
    public string SettingsConnectionDefaultBaudRate => _localization.GetString("settings.connection.defaultBaudRate");
    public string SettingsConnectionDefaultEncoding => _localization.GetString("settings.connection.defaultEncoding");
    public string SettingsConnectionDefaultAddCr => _localization.GetString("settings.connection.defaultAddCr");
    public string SettingsConnectionDefaultAddLf => _localization.GetString("settings.connection.defaultAddLf");
    public string SettingsConnectionExistingSessionBehavior => _localization.GetString("settings.connection.existingSessionBehavior");
    public string SettingsConnectionBehaviorCreateNew => _localization.GetString("settings.connection.behavior.createNew");
    public string SettingsConnectionBehaviorSwitchToExisting => _localization.GetString("settings.connection.behavior.switchToExisting");
    public string SettingsConnectionBehaviorPromptUser => _localization.GetString("settings.connection.behavior.promptUser");
    public string SettingsConnectionLinuxScan => _localization.GetString("settings.connection.linuxScan");
    public string SettingsConnectionLinuxScanScanPatterns => _localization.GetString("settings.connection.linuxScan.scanPatterns");
    public string SettingsConnectionLinuxScanExcludePatterns => _localization.GetString("settings.connection.linuxScan.excludePatterns");
    public string SettingsConnectionLinuxScanTip => _localization.GetString("settings.connection.linuxScan.tip");
    public string SettingsDisplayMaxMessages => _localization.GetString("settings.display.maxMessages");
    public string SettingsDisplayAutoScroll => _localization.GetString("settings.display.autoScroll");
    public string SettingsDisplayTimestampFormat => _localization.GetString("settings.display.timestampFormat");
    public string SettingsDisplayFontFamily => _localization.GetString("settings.display.fontFamily");
    public string SettingsDisplayFontSize => _localization.GetString("settings.display.fontSize");
    public string SettingsExportDefaultFormat => _localization.GetString("settings.export.defaultFormat");
    public string SettingsExportDefaultDirectory => _localization.GetString("settings.export.defaultDirectory");
    public string SettingsExportRange => _localization.GetString("settings.export.range");
    public string SettingsExportRangeAll => _localization.GetString("settings.export.range.all");
    public string SettingsExportRangeLatest => _localization.GetString("settings.export.range.latest");
    public string SettingsExportRangeCount => _localization.GetString("settings.export.range.count");
    public string SettingsClose => _localization.GetString("settings.actions.close");

    // Notifications
    public string NotificationsTitle => _localization.GetString("notifications.title");
    public string NotificationsEmpty => _localization.GetString("notifications.empty");
    public string NotificationsMarkAllRead => _localization.GetString("notifications.markAllRead");
    public string NotificationsClearAll => _localization.GetString("notifications.clearAll");
    public string NotificationsDelete => _localization.GetString("notifications.delete");

    // Tool Send - Additional
    public string ToolSendClearAfterSend => _localization.GetString("tool.send.clearAfterSend");

    // Session Menu
    public string SessionMenuRename => _localization.GetString("session.menu.rename");
    public string SessionMenuDisconnect => _localization.GetString("session.menu.disconnect");
    public string SessionMenuDelete => _localization.GetString("session.menu.delete");

    // Session Rename Dialog
    public string SessionRenameTitle => _localization.GetString("session.rename.title");
    public string SessionRenameLabel => _localization.GetString("session.rename.label");
    public string SessionRenamePlaceholder => _localization.GetString("session.rename.placeholder");
    public string SessionRenameOk => _localization.GetString("session.rename.ok");
    public string SessionRenameCancel => _localization.GetString("session.rename.cancel");

    // Connection Errors
    public string ConnectionErrorNoPortSelected => _localization.GetString("connection.error.noPortSelected");
    public string ConnectionErrorNoPortSelectedMessage => _localization.GetString("connection.error.noPortSelectedMessage");
    public string ConnectionErrorFailed => _localization.GetString("connection.error.failed");
    
    // Connection Confirm
    public string ConnectionConfirmExistingSessionTitle => _localization.GetString("connection.confirm.existingSession.title");
    public string ConnectionConfirmExistingSessionMessage => _localization.GetString("connection.confirm.existingSession.message");
    public string ConnectionConfirmOk => _localization.GetString("connection.confirm.ok");
    public string ConnectionConfirmCancel => _localization.GetString("connection.confirm.cancel");

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshStrings()
    {
        OnPropertyChanged(string.Empty); // Notify all properties changed
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
