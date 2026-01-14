namespace ComCross.Shared.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "en-US";
    public bool FollowSystemLanguage { get; set; } = true;
    public LogSettings Logs { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
}

public sealed class LogSettings
{
    public bool AutoSaveEnabled { get; set; } = true;
    public string Directory { get; set; } = string.Empty;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxTotalSizeMb { get; set; } = 256;
    public bool AutoDeleteEnabled { get; set; }
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
}

public sealed class DisplaySettings
{
    public int MaxMessages { get; set; } = 10000;
    public bool AutoScroll { get; set; } = true;
    public string TimestampFormat { get; set; } = "HH:mm:ss.fff";
}

public sealed class ExportSettings
{
    public string DefaultFormat { get; set; } = "txt";
    public string DefaultDirectory { get; set; } = string.Empty;
}
