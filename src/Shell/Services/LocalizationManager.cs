using System.ComponentModel;
using ComCross.Core.Services;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

/// <summary>
/// Static localization manager for XAML bindings
/// </summary>
public static class LocalizationManager
{
    private static readonly ILocalizationService _localization;

    static LocalizationManager()
    {
        _localization = new LocalizationService();
    }

    // Main Window
    public static string AppTitle => _localization.GetString("app.title");
    public static string MenuConnect => _localization.GetString("menu.connect");
    public static string MenuDisconnect => _localization.GetString("menu.disconnect");
    public static string MenuClear => _localization.GetString("menu.clear");
    public static string MenuExport => _localization.GetString("menu.export");

    // Connect Dialog
    public static string DialogConnectTitle => _localization.GetString("dialog.connect.title");
    public static string DialogConnectPort => _localization.GetString("dialog.connect.port");
    public static string DialogConnectBaudRate => _localization.GetString("dialog.connect.baudrate");
    public static string DialogConnectSessionName => _localization.GetString("dialog.connect.sessionname");
    public static string DialogConnectSessionNamePlaceholder => _localization.GetString("dialog.connect.sessionname.placeholder");
    public static string DialogConnectCancel => _localization.GetString("dialog.connect.cancel");
    public static string DialogConnectConnect => _localization.GetString("dialog.connect.connect");

    // Sidebar
    public static string SidebarDevices => _localization.GetString("sidebar.devices");
    public static string SidebarSessions => _localization.GetString("sidebar.sessions");

    // Message Stream
    public static string StreamSearchPlaceholder => _localization.GetString("stream.search.placeholder");
    public static string StreamMetricsRx => _localization.GetString("stream.metrics.rx");
    public static string StreamMetricsTx => _localization.GetString("stream.metrics.tx");
    public static string StreamMetricsLines => _localization.GetString("stream.metrics.lines");

    // Tool Dock
    public static string ToolSend => _localization.GetString("tool.send");
    public static string ToolFilter => _localization.GetString("tool.filter");
    public static string ToolHighlight => _localization.GetString("tool.highlight");
    public static string ToolExport => _localization.GetString("tool.export");
    public static string ToolSendQuickCommands => _localization.GetString("tool.send.quickcommands");
    public static string ToolSendMessage => _localization.GetString("tool.send.message");
    public static string ToolSendMessagePlaceholder => _localization.GetString("tool.send.message.placeholder");
    public static string ToolSendHexMode => _localization.GetString("tool.send.hexmode");
    public static string ToolSendAddCr => _localization.GetString("tool.send.addcr");
    public static string ToolSendAddLf => _localization.GetString("tool.send.addlf");
    public static string ToolSendButton => _localization.GetString("tool.send.button");
    public static string ToolSendCmdStatus => _localization.GetString("tool.send.cmd.status");
    public static string ToolSendCmdReset => _localization.GetString("tool.send.cmd.reset");
    public static string ToolSendCmdGetConfig => _localization.GetString("tool.send.cmd.getconfig");

    // Status Bar
    public static string StatusReady => _localization.GetString("status.ready");
}
