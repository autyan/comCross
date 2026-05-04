namespace ComCross.Shared.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "en-US";
    public bool FollowSystemLanguage { get; set; } = true;
    public AppLogSettings AppLogs { get; set; } = new();

    /// <summary>
    /// Session Logs settings. The persisted property name remains Logs during the pre-stable transition.
    /// </summary>
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
    public const int SettingsSchemaVersion = 1;

    public int SchemaVersion { get; set; } = SettingsSchemaVersion;
    public bool AutoSaveEnabled { get; set; } = true;
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Segment rollover size for the live spool working format.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Global maximum Session Logs spool size.
    /// </summary>
    public int MaxTotalSizeMb { get; set; } = 256;

    /// <summary>
    /// Per-session maximum Session Logs spool size. A value below 1 lets Core derive a safe default.
    /// </summary>
    public int MaxPerSessionSizeMb { get; set; } = 64;

    public bool AutoDeleteEnabled { get; set; }

    /// <summary>
    /// Legacy global database persistence flag. Archive is session-scoped and must not use this as a default enable switch.
    /// </summary>
    public bool DatabasePersistenceEnabled { get; set; }

    /// <summary>
    /// Legacy database directory. Session Archive storage is per-session and resolved by Core storage services.
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

    public SessionLogExportFormat DefaultSessionLogFormat { get; set; } = SessionLogExportFormat.Plain;
    public PayloadRenderMode DefaultPayloadRenderMode { get; set; } = PayloadRenderMode.String;

    /// <summary>
    /// Legacy export range option. v0.6 Session Logs export is complete-source only.
    /// </summary>
    public ExportRangeMode RangeMode { get; set; } = ExportRangeMode.All;

    /// <summary>
    /// Legacy export count option. v0.6 Session Logs export is complete-source only.
    /// </summary>
    public int RangeCount { get; set; } = 1000;
}

public enum ExportRangeMode
{
    All,
    Latest
}

public sealed class CommandSettings
{
    public bool DefaultsInitialized { get; set; }
    public List<CommandDefinition> GlobalCommands { get; set; } = new();
    public Dictionary<string, List<CommandDefinition>> SessionCommands { get; set; } = new();
}

public sealed class PluginSettings
{
    public Dictionary<string, bool> Enabled { get; set; } = new();

    /// <summary>
    /// Optional plugin package trust enforcement.
    /// Default is disabled for development/backward compatibility.
    /// </summary>
    public PluginSignatureVerificationSettings SignatureVerification { get; set; } = new();
}

public sealed class PluginSignatureVerificationSettings
{
    /// <summary>
    /// When enabled, Core will evaluate whether a plugin package is trusted before starting its host.
    /// Default: disabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Development-only unsigned plugin allow-list.
    /// NOTE: This is not a cryptographic guarantee and must not be used for Stable/EAP enforcement.
    /// </summary>
    public List<string> AllowUnsignedPluginIds { get; set; } = new();
}
