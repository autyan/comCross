using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ComCross.Shared.Services;

namespace ComCross.Core.Services;

/// <summary>
/// JSON-based localization service implementation
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new();
    private string _currentCulture = "en-US";

    public string CurrentCulture => _currentCulture;

    public IReadOnlyList<LocaleCultureInfo> AvailableCultures { get; }

    public LocalizationService()
    {
        AvailableCultures = new List<LocaleCultureInfo>
        {
            new("en-US", "English", "English"),
            new("zh-CN", "Chinese (Simplified)", "简体中文")
        };

        // Load default culture
        LoadCulture(_currentCulture);
    }

    public void SetCulture(string cultureCode)
    {
        if (string.IsNullOrEmpty(cultureCode))
        {
            throw new ArgumentException("Culture code cannot be null or empty", nameof(cultureCode));
        }

        _currentCulture = cultureCode;
        LoadCulture(cultureCode);
    }

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_translations.TryGetValue(_currentCulture, out var culture))
        {
            if (culture.TryGetValue(key, out var value))
            {
                return args.Length > 0 ? string.Format(value, args) : value;
            }
        }

        // Fallback to en-US
        if (_currentCulture != "en-US" && _translations.TryGetValue("en-US", out var fallback))
        {
            if (fallback.TryGetValue(key, out var value))
            {
                return args.Length > 0 ? string.Format(value, args) : value;
            }
        }

        return $"[{key}]"; // Return key if not found
    }

    private void LoadCulture(string cultureCode)
    {
        if (_translations.ContainsKey(cultureCode))
        {
            return; // Already loaded
        }

        // Use built-in translations (embedded in code for reliability)
        _translations[cultureCode] = cultureCode == "zh-CN" 
            ? GetChineseTranslations() 
            : GetEnglishTranslations();
    }

    private static Dictionary<string, string> GetEnglishTranslations()
    {
        return new Dictionary<string, string>
        {
            // Main Window
            ["app.title"] = "ComCross - Serial Toolbox",
            ["menu.connect"] = "Connect",
            ["menu.disconnect"] = "Disconnect",
            ["menu.clear"] = "Clear",
            ["menu.export"] = "Export",
            
            // Connect Dialog
            ["dialog.connect.title"] = "Connect to Device",
            ["dialog.connect.port"] = "Port",
            ["dialog.connect.baudrate"] = "Baud Rate",
            ["dialog.connect.sessionname"] = "Session Name",
            ["dialog.connect.sessionname.placeholder"] = "My Session",
            ["dialog.connect.cancel"] = "Cancel",
            ["dialog.connect.connect"] = "Connect",
            
            // Sidebar
            ["sidebar.devices"] = "DEVICES",
            ["sidebar.sessions"] = "SESSIONS",
            
            // Message Stream
            ["stream.search.placeholder"] = "Search messages...",
            ["stream.metrics.rx"] = "RX:",
            ["stream.metrics.tx"] = "TX:",
            ["stream.metrics.lines"] = "Lines:",
            
            // Tool Dock
            ["tool.send"] = "Send",
            ["tool.filter"] = "Filter",
            ["tool.highlight"] = "Highlight",
            ["tool.export"] = "Export",
            ["tool.send.quickcommands"] = "QUICK COMMANDS",
            ["tool.send.message"] = "MESSAGE",
            ["tool.send.message.placeholder"] = "Type your message...",
            ["tool.send.hexmode"] = "HEX Mode",
            ["tool.send.addcr"] = "Add CR",
            ["tool.send.addlf"] = "Add LF",
            ["tool.send.button"] = "Send",
            ["tool.send.cmd.status"] = "Status",
            ["tool.send.cmd.reset"] = "Reset",
            ["tool.send.cmd.getconfig"] = "Get Config",
            
            // Status Bar
            ["status.ready"] = "Ready",
            ["status.connected"] = "Connected",
            ["status.disconnected"] = "Disconnected",
            ["status.rxbytes"] = "RX: {0} bytes",
            ["status.txbytes"] = "TX: {0} bytes",
            
            // Settings
            ["settings.language"] = "Language",
            ["settings.theme"] = "Theme",
            ["settings.autosave"] = "Auto Save"
        };
    }

    private static Dictionary<string, string> GetChineseTranslations()
    {
        return new Dictionary<string, string>
        {
            // Main Window
            ["app.title"] = "ComCross - 串口工具箱",
            ["menu.connect"] = "连接",
            ["menu.disconnect"] = "断开",
            ["menu.clear"] = "清空",
            ["menu.export"] = "导出",
            
            // Connect Dialog
            ["dialog.connect.title"] = "连接到设备",
            ["dialog.connect.port"] = "端口",
            ["dialog.connect.baudrate"] = "波特率",
            ["dialog.connect.sessionname.placeholder"] = "我的会话",
            ["dialog.connect.sessionname"] = "会话名称",
            ["dialog.connect.cancel"] = "取消",
            ["dialog.connect.connect"] = "连接",
            
            // Sidebar
            ["sidebar.devices"] = "设备",
            ["sidebar.sessions"] = "会话",
            
            // Message Stream
            ["stream.search.placeholder"] = "搜索消息...",
            ["stream.metrics.rx"] = "接收:",
            ["stream.metrics.tx"] = "发送:",
            ["stream.metrics.lines"] = "行数:",
            
            // Tool Dock
            ["tool.send"] = "发送",
            ["tool.filter"] = "过滤",
            ["tool.highlight"] = "高亮",
            ["tool.export"] = "导出",
            ["tool.send.quickcommands"] = "快捷命令",
            ["tool.send.message.placeholder"] = "输入消息...",
            ["tool.send.message"] = "消息",
            ["tool.send.hexmode"] = "十六进制模式",
            ["tool.send.addcr"] = "添加 CR",
            ["tool.send.addlf"] = "添加 LF",
            ["tool.send.button"] = "发送",
            ["tool.send.cmd.status"] = "状态",
            ["tool.send.cmd.reset"] = "重置",
            ["tool.send.cmd.getconfig"] = "获取配置",
            
            // Status Bar
            ["status.ready"] = "就绪",
            ["status.connected"] = "已连接",
            ["status.disconnected"] = "已断开",
            ["status.rxbytes"] = "接收: {0} 字节",
            ["status.txbytes"] = "发送: {0} 字节",
            
            // Settings
            ["settings.language"] = "语言",
            ["settings.theme"] = "主题",
            ["settings.autosave"] = "自动保存"
        };
    }
}
