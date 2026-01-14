using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ComCross.Assets;
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
        var defaultTranslations = GetEnglishTranslations();
        _translations["en-US"] = defaultTranslations;

        AvailableCultures = LoadResourceTranslations(defaultTranslations);

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

    private IReadOnlyList<LocaleCultureInfo> LoadResourceTranslations(
        Dictionary<string, string> defaultTranslations)
    {
        var cultures = new List<LocaleCultureInfo>
        {
            new("en-US", "English", "English")
        };

        var assembly = typeof(AssetMarker).Assembly;
        var resourceName = "ComCross.Assets.Resources.Localization.strings.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return cultures;
        }

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("cultures", out var culturesElement))
        {
            return cultures;
        }

        foreach (var cultureProperty in culturesElement.EnumerateObject())
        {
            var cultureCode = cultureProperty.Name;
            var strings = new Dictionary<string, string>();
            foreach (var kvp in cultureProperty.Value.EnumerateObject())
            {
                strings[kvp.Name] = kvp.Value.GetString() ?? string.Empty;
            }

            if (cultureCode == "en-US")
            {
                foreach (var kvp in strings)
                {
                    defaultTranslations[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                _translations[cultureCode] = strings;
            }

            if (cultureCode != "en-US")
            {
                cultures.Add(CreateLocaleCultureInfo(cultureCode));
            }
        }

        return cultures;
    }

    private static LocaleCultureInfo CreateLocaleCultureInfo(string cultureCode)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureCode);
            return new LocaleCultureInfo(culture.Name, culture.EnglishName, culture.NativeName);
        }
        catch (CultureNotFoundException)
        {
            return new LocaleCultureInfo(cultureCode, cultureCode, cultureCode);
        }
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
            ["settings.title"] = "Settings",
            ["settings.section.general"] = "General",
            ["settings.section.logs"] = "Logs",
            ["settings.section.notifications"] = "Notifications",
            ["settings.section.connection"] = "Connection",
            ["settings.section.display"] = "Display",
            ["settings.section.export"] = "Export",
            ["settings.language"] = "Language",
            ["settings.followSystemLanguage"] = "Follow system language",
            ["settings.logs.autosave"] = "Auto save logs",
            ["settings.logs.directory"] = "Log directory",
            ["settings.logs.maxFileSize"] = "Max file size (MB)",
            ["settings.logs.maxTotalSize"] = "Max total size (MB)",
            ["settings.logs.autoDelete"] = "Auto delete when exceeded",
            ["settings.logs.autoDeleteRuleTip"] = "When enabled, the oldest log files are deleted until the total size is below the limit.",
            ["settings.notifications.storage"] = "Storage limit alerts",
            ["settings.notifications.connection"] = "Connection alerts",
            ["settings.notifications.export"] = "Export alerts",
            ["settings.notifications.retentionDays"] = "Retention days",
            ["settings.connection.defaultBaudRate"] = "Default baud rate",
            ["settings.connection.defaultEncoding"] = "Default encoding",
            ["settings.connection.defaultAddCr"] = "Append CR by default",
            ["settings.connection.defaultAddLf"] = "Append LF by default",
            ["settings.display.maxMessages"] = "Max in-memory messages",
            ["settings.display.autoScroll"] = "Auto scroll",
            ["settings.display.timestampFormat"] = "Timestamp format",
            ["settings.export.defaultFormat"] = "Default format",
            ["settings.export.defaultDirectory"] = "Default export directory",
            ["settings.actions.close"] = "Close",

            // Notifications
            ["notifications.title"] = "Notifications",
            ["notifications.empty"] = "No notifications",
            ["notifications.markAllRead"] = "Mark all read",
            ["notification.storage.limitExceeded"] = "Log storage limit exceeded ({0} MB / {1} MB).",
            ["notification.storage.autoDeleteApplied"] = "Auto delete removed {0} log files.",
            ["notification.connection.disconnected"] = "Session {0} disconnected ({1}).",
            ["notification.connection.unknownReason"] = "unknown",
            ["notification.export.completed"] = "Export completed: {0}"
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
            ["settings.title"] = "设置",
            ["settings.section.general"] = "通用",
            ["settings.section.logs"] = "日志",
            ["settings.section.notifications"] = "通知",
            ["settings.section.connection"] = "连接",
            ["settings.section.display"] = "显示",
            ["settings.section.export"] = "导出",
            ["settings.language"] = "语言",
            ["settings.followSystemLanguage"] = "跟随系统语言",
            ["settings.logs.autosave"] = "自动保存日志",
            ["settings.logs.directory"] = "日志目录",
            ["settings.logs.maxFileSize"] = "单文件上限 (MB)",
            ["settings.logs.maxTotalSize"] = "总占用上限 (MB)",
            ["settings.logs.autoDelete"] = "超限自动删除",
            ["settings.logs.autoDeleteRuleTip"] = "开启后将按时间删除最旧日志，直到总占用低于上限。",
            ["settings.notifications.storage"] = "存储超限提醒",
            ["settings.notifications.connection"] = "连接异常提醒",
            ["settings.notifications.export"] = "导出完成提醒",
            ["settings.notifications.retentionDays"] = "保留天数",
            ["settings.connection.defaultBaudRate"] = "默认波特率",
            ["settings.connection.defaultEncoding"] = "默认编码",
            ["settings.connection.defaultAddCr"] = "默认追加 CR",
            ["settings.connection.defaultAddLf"] = "默认追加 LF",
            ["settings.display.maxMessages"] = "内存消息上限",
            ["settings.display.autoScroll"] = "自动滚动",
            ["settings.display.timestampFormat"] = "时间戳格式",
            ["settings.export.defaultFormat"] = "默认格式",
            ["settings.export.defaultDirectory"] = "默认导出目录",
            ["settings.actions.close"] = "关闭",

            // Notifications
            ["notifications.title"] = "通知中心",
            ["notifications.empty"] = "暂无通知",
            ["notifications.markAllRead"] = "全部已读",
            ["notification.storage.limitExceeded"] = "日志占用超限（{0} MB / {1} MB）。",
            ["notification.storage.autoDeleteApplied"] = "自动删除了 {0} 个日志文件。",
            ["notification.connection.disconnected"] = "会话 {0} 断开（{1}）。",
            ["notification.connection.unknownReason"] = "未知",
            ["notification.export.completed"] = "导出完成：{0}"
        };
    }
}
