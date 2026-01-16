using ComCross.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Services;

/// <summary>
/// Static localization manager for XAML bindings
/// </summary>
public static class LocalizationManager
{
    private static ILocalizationService Localization => App.ServiceProvider.GetRequiredService<ILocalizationService>();

    // Main Window
    public static string AppTitle => Localization.GetString("app.title");
    public static string MenuConnect => Localization.GetString("menu.connect");
    public static string MenuDisconnect => Localization.GetString("menu.disconnect");
    public static string MenuClear => Localization.GetString("menu.clear");
    public static string MenuExport => Localization.GetString("menu.export");

    // Connect Dialog
    public static string DialogConnectTitle => Localization.GetString("dialog.connect.title");
    public static string DialogConnectPort => Localization.GetString("dialog.connect.port");
    public static string DialogConnectBaudRate => Localization.GetString("dialog.connect.baudrate");
    public static string DialogConnectSessionName => Localization.GetString("dialog.connect.sessionname");
    public static string DialogConnectSessionNamePlaceholder => Localization.GetString("dialog.connect.sessionname.placeholder");
    public static string DialogConnectCancel => Localization.GetString("dialog.connect.cancel");
    public static string DialogConnectConnect => Localization.GetString("dialog.connect.connect");

    // Sidebar
    public static string SidebarDevices => Localization.GetString("sidebar.devices");
    public static string SidebarSessions => Localization.GetString("sidebar.sessions");

    // Message Stream
    public static string StreamSearchPlaceholder => Localization.GetString("stream.search.placeholder");
    public static string StreamMetricsRx => Localization.GetString("stream.metrics.rx");
    public static string StreamMetricsTx => Localization.GetString("stream.metrics.tx");
    public static string StreamMetricsLines => Localization.GetString("stream.metrics.lines");

    // Tool Dock
    public static string ToolSend => Localization.GetString("tool.send");
    public static string ToolFilter => Localization.GetString("tool.filter");
    public static string ToolHighlight => Localization.GetString("tool.highlight");
    public static string ToolExport => Localization.GetString("tool.export");
    public static string ToolSendQuickCommands => Localization.GetString("tool.send.quickcommands");
    public static string ToolSendMessage => Localization.GetString("tool.send.message");
    public static string ToolSendMessagePlaceholder => Localization.GetString("tool.send.message.placeholder");
    public static string ToolSendHexMode => Localization.GetString("tool.send.hexmode");
    public static string ToolSendAddCr => Localization.GetString("tool.send.addcr");
    public static string ToolSendAddLf => Localization.GetString("tool.send.addlf");
    public static string ToolSendButton => Localization.GetString("tool.send.button");
    public static string ToolSendCmdStatus => Localization.GetString("tool.send.cmd.status");
    public static string ToolSendCmdReset => Localization.GetString("tool.send.cmd.reset");
    public static string ToolSendCmdGetConfig => Localization.GetString("tool.send.cmd.getconfig");

    // Status Bar
    public static string StatusReady => Localization.GetString("status.ready");
}
