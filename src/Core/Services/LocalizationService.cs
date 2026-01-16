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

        // English is hardcoded (always available)
        // Other languages must be loaded from strings.json
        if (cultureCode == "en-US")
        {
            _translations[cultureCode] = GetEnglishTranslations();
        }
        // For other cultures, they should have been loaded from JSON in constructor
        // If not found, they will fallback to en-US automatically
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
            
            // Skip en-US in JSON - we only use hardcoded English
            if (cultureCode == "en-US")
            {
                continue;
            }
            
            var strings = new Dictionary<string, string>();
            foreach (var kvp in cultureProperty.Value.EnumerateObject())
            {
                strings[kvp.Name] = kvp.Value.GetString() ?? string.Empty;
            }

            _translations[cultureCode] = strings;
            cultures.Add(CreateLocaleCultureInfo(cultureCode));
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
            
            // Workload
            ["workload.new"] = "New Workload",
            ["workload.close"] = "Close",
            ["workload.rename"] = "Rename",
            ["workload.copy"] = "Copy",
            ["workload.delete"] = "Delete",
            ["workload.default.name"] = "{0}'s Workspace",
            ["workload.panel.title"] = "Workloads",
            ["workload.panel.loading"] = "Loading...",
            
            // Workload Dialogs
            ["dialog.createWorkload.title"] = "Create Workload",
            ["dialog.createWorkload.name"] = "Workload Name *",
            ["dialog.createWorkload.description"] = "Description (Optional)",
            ["dialog.createWorkload.cancel"] = "Cancel",
            ["dialog.createWorkload.create"] = "Create",
            ["dialog.renameWorkload.title"] = "Rename Workload",
            ["dialog.renameWorkload.name"] = "New Name *",
            ["dialog.renameWorkload.cancel"] = "Cancel",
            ["dialog.renameWorkload.rename"] = "Rename",
            
            // Main Actions
            ["action.settings"] = "Settings",
            ["action.notifications"] = "Notifications",
            
            // Sidebar
            ["sidebar.workloads"] = "WORKLOADS",
            ["sidebar.devices"] = "DEVICES",
            ["sidebar.sessions"] = "SESSIONS",
            ["sidebar.busAdapter"] = "BUS ADAPTER",
            ["sidebar.deviceConfig"] = "DEVICE CONFIGURATION",
            ["sidebar.selectPort"] = "Select port",
            ["sidebar.refreshPorts"] = "Refresh ports",
            ["sidebar.quickConnect"] = "Quick Connect",
            ["sidebar.baudRate"] = "Baud Rate",
            ["sidebar.dataBits"] = "Data Bits",
            ["sidebar.parity"] = "Parity",
            ["sidebar.stopBits"] = "Stop Bits",
            ["sidebar.parity.none"] = "None",
            ["sidebar.parity.odd"] = "Odd",
            ["sidebar.parity.even"] = "Even",
            ["sidebar.newSession"] = "New Session",
            
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
            ["tool.commands"] = "Commands",
            ["tool.commands.empty"] = "No commands",
            ["tool.commands.send"] = "Send",
            ["tool.commands.add"] = "New",
            ["tool.commands.save"] = "Save",
            ["tool.commands.delete"] = "Delete",
            ["tool.commands.import"] = "Import",
            ["tool.commands.export"] = "Export",
            ["tool.commands.name"] = "Name",
            ["tool.commands.payload"] = "Payload",
            ["tool.commands.type"] = "Type",
            ["tool.commands.encoding"] = "Encoding",
            ["tool.commands.group"] = "Group",
            ["tool.commands.scope"] = "Scope",
            ["tool.commands.appendCr"] = "Append CR",
            ["tool.commands.appendLf"] = "Append LF",
            ["tool.commands.hotkey"] = "Hotkey",
            ["tool.commands.sortOrder"] = "Order",
            ["tool.commands.scope.global"] = "Global",
            ["tool.commands.scope.session"] = "Session",
            ["tool.commands.type.text"] = "Text",
            ["tool.commands.type.hex"] = "Hex",
            
            // Status Bar
            ["status.ready"] = "Ready",
            ["status.connected"] = "Connected",
            ["status.disconnected"] = "Disconnected",
            ["status.rxbytes"] = "RX: {0} bytes",
            ["status.txbytes"] = "TX: {0} bytes",
            ["status.rx"] = "RX:",
            ["status.tx"] = "TX:",
            ["status.cpu"] = "CPU:",
            ["status.mem"] = "MEM:",
            
            // Settings
            ["settings.title"] = "Settings",
            ["settings.section.general"] = "General",
            ["settings.section.logs"] = "Logs",
            ["settings.section.appLogs"] = "Application Logs",
            ["settings.section.notifications"] = "Notifications",
            ["settings.section.connection"] = "Connection",
            ["settings.section.display"] = "Display",
            ["settings.section.export"] = "Export",
            ["settings.section.plugins"] = "Plugins",
            ["settings.language"] = "Language",
            ["settings.followSystemLanguage"] = "Follow system language",
            ["settings.logs.autosave"] = "Auto save logs",
            ["settings.logs.directory"] = "Log directory",
            ["settings.logs.maxFileSize"] = "Max file size (MB)",
            ["settings.logs.maxTotalSize"] = "Max total size (MB)",
            ["settings.logs.autoDelete"] = "Auto delete when exceeded",
            ["settings.logs.autoDeleteRuleTip"] = "When enabled, the oldest log files are deleted until the total size is below the limit.",
            ["settings.logs.enableDatabase"] = "Enable database persistence (SQLite)",
            ["settings.logs.databaseWarning"] = "Enabling database persistence will use more storage and may impact performance. Messages will be stored in SQLite for advanced search capabilities.",
            ["settings.logs.databaseDirectory"] = "Database directory",
            ["session.database.enable"] = "DB Store",
            ["session.database.tooltip"] = "Store messages to database for advanced search. Note: Historical data is not converted. Switching will result in data loss unless manually imported.",
            ["sidebar.busAdapter.comingSoon"] = "(Coming soon)",
            ["action.notifications.tooltip"] = "Notifications",
            ["action.settings.tooltip"] = "Settings",
            ["settings.appLogs.enabled"] = "Enable application logs",
            ["settings.appLogs.directory"] = "App log directory",
            ["settings.appLogs.format"] = "Log format",
            ["settings.appLogs.minLevel"] = "Minimum level",
            ["settings.plugins.enabled"] = "Enabled",
            ["settings.plugins.name"] = "Name",
            ["settings.plugins.permissions"] = "Permissions",
            ["settings.plugins.path"] = "Path",
            ["settings.plugins.status.loaded"] = "Loaded",
            ["settings.plugins.status.disabled"] = "Disabled",
            ["settings.plugins.status.failed"] = "Failed",
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
            ["settings.export.range"] = "Range",
            ["settings.export.range.all"] = "All",
            ["settings.export.range.latest"] = "Latest",
            ["settings.export.range.count"] = "Count",
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
}
