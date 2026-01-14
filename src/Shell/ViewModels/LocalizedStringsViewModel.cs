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

    // Status Bar
    public string StatusReady => _localization.GetString("status.ready");

    // Settings
    public string SettingsTitle => _localization.GetString("settings.title");
    public string SettingsSectionGeneral => _localization.GetString("settings.section.general");
    public string SettingsSectionLogs => _localization.GetString("settings.section.logs");
    public string SettingsSectionNotifications => _localization.GetString("settings.section.notifications");
    public string SettingsSectionConnection => _localization.GetString("settings.section.connection");
    public string SettingsSectionDisplay => _localization.GetString("settings.section.display");
    public string SettingsSectionExport => _localization.GetString("settings.section.export");
    public string SettingsLanguage => _localization.GetString("settings.language");
    public string SettingsFollowSystemLanguage => _localization.GetString("settings.followSystemLanguage");
    public string SettingsLogsAutoSave => _localization.GetString("settings.logs.autosave");
    public string SettingsLogsDirectory => _localization.GetString("settings.logs.directory");
    public string SettingsLogsMaxFileSize => _localization.GetString("settings.logs.maxFileSize");
    public string SettingsLogsMaxTotalSize => _localization.GetString("settings.logs.maxTotalSize");
    public string SettingsLogsAutoDelete => _localization.GetString("settings.logs.autoDelete");
    public string SettingsLogsAutoDeleteRuleTip => _localization.GetString("settings.logs.autoDeleteRuleTip");
    public string SettingsNotificationsStorage => _localization.GetString("settings.notifications.storage");
    public string SettingsNotificationsConnection => _localization.GetString("settings.notifications.connection");
    public string SettingsNotificationsExport => _localization.GetString("settings.notifications.export");
    public string SettingsNotificationsRetentionDays => _localization.GetString("settings.notifications.retentionDays");
    public string SettingsConnectionDefaultBaudRate => _localization.GetString("settings.connection.defaultBaudRate");
    public string SettingsConnectionDefaultEncoding => _localization.GetString("settings.connection.defaultEncoding");
    public string SettingsConnectionDefaultAddCr => _localization.GetString("settings.connection.defaultAddCr");
    public string SettingsConnectionDefaultAddLf => _localization.GetString("settings.connection.defaultAddLf");
    public string SettingsDisplayMaxMessages => _localization.GetString("settings.display.maxMessages");
    public string SettingsDisplayAutoScroll => _localization.GetString("settings.display.autoScroll");
    public string SettingsDisplayTimestampFormat => _localization.GetString("settings.display.timestampFormat");
    public string SettingsExportDefaultFormat => _localization.GetString("settings.export.defaultFormat");
    public string SettingsExportDefaultDirectory => _localization.GetString("settings.export.defaultDirectory");
    public string SettingsClose => _localization.GetString("settings.actions.close");

    // Notifications
    public string NotificationsTitle => _localization.GetString("notifications.title");
    public string NotificationsEmpty => _localization.GetString("notifications.empty");
    public string NotificationsMarkAllRead => _localization.GetString("notifications.markAllRead");

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
