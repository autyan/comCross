namespace ComCross.Shared.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "en-US";
    public bool FollowSystemLanguage { get; set; } = true;
    public AppLogSettings AppLogs { get; set; } = new();
    public LogSettings Logs { get; set; } = new();
    public CommandSettings Commands { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
    public PluginSettings Plugins { get; set; } = new();
}

public sealed class AppLogSettings
{
    public bool Enabled { get; set; } = true;
    public string Directory { get; set; } = string.Empty;
    public string Format { get; set; } = "txt";
    public string MinLevel { get; set; } = "Info";
}

public sealed class LogSettings
{
    public bool AutoSaveEnabled { get; set; } = true;
    public string Directory { get; set; } = string.Empty;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxTotalSizeMb { get; set; } = 256;
    public bool AutoDeleteEnabled { get; set; }
    
    /// <summary>
    /// Enable database persistence for message storage (default: enabled)
    /// </summary>
    public bool DatabasePersistenceEnabled { get; set; } = true;
    
    /// <summary>
    /// Database directory (optional, defaults to AppData if not specified)
    /// </summary>
    public string? DatabaseDirectory { get; set; }
}

public sealed class NotificationSettings
{
    public bool StorageAlertsEnabled { get; set; } = true;
    public bool ConnectionAlertsEnabled { get; set; } = true;
    public bool ExportAlertsEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 7;
}

public sealed class ConnectionSettings
{
    public int DefaultBaudRate { get; set; } = 115200;
    public string DefaultEncoding { get; set; } = "UTF-8";
    public bool DefaultAddCr { get; set; } = true;
    public bool DefaultAddLf { get; set; } = true;
    public ConnectionBehavior ExistingSessionBehavior { get; set; } = ConnectionBehavior.PromptUser;
}

public enum ConnectionBehavior
{
    CreateNew,
    SwitchToExisting,
    PromptUser
}

public sealed class DisplaySettings
{
    public int MaxMessages { get; set; } = 10000;
    public bool AutoScroll { get; set; } = true;
    public string TimestampFormat { get; set; } = "HH:mm:ss.fff";
    public string FontFamily { get; set; } = GetDefaultFontFamily();
    public int FontSize { get; set; } = 11;

    private static string GetDefaultFontFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Consolas";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return "Menlo";
        }
        else // Linux
        {
            return "DejaVu Sans Mono";
        }
    }
}

public sealed class ExportSettings
{
    public string DefaultFormat { get; set; } = "txt";
    public string DefaultDirectory { get; set; } = string.Empty;
    public ExportRangeMode RangeMode { get; set; } = ExportRangeMode.All;
    public int RangeCount { get; set; } = 1000;
}

public enum ExportRangeMode
{
    All,
    Latest
}

public sealed class CommandSettings
{
    public List<CommandDefinition> GlobalCommands { get; set; } = new();
    public Dictionary<string, List<CommandDefinition>> SessionCommands { get; set; } = new();
}

public sealed class PluginSettings
{
    public Dictionary<string, bool> Enabled { get; set; } = new();
}
