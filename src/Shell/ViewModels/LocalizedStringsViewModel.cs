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
