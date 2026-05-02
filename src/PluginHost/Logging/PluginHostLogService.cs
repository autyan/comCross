using System.Globalization;
using System.Runtime.InteropServices;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace ComCross.PluginHost.Logging;

public sealed class PluginHostLogService
{
    private readonly Logger _logger;
    private PluginHostLogOptions? _options;

    public PluginHostLogService(string loggerName = "ComCross.PluginHost")
    {
        _logger = LogManager.GetLogger(string.IsNullOrWhiteSpace(loggerName) ? "ComCross.PluginHost" : loggerName);
    }

    public void Initialize(PluginHostLogOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var directory = string.IsNullOrWhiteSpace(options.Directory)
            ? ResolveDefaultDirectory()
            : options.Directory;

        Directory.CreateDirectory(directory);

        var fileKey = SanitizeFileComponent(options.FileKey);
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            fileKey = "unknown";
        }

        var extension = string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "log";

        var fileName = Path.Combine(directory, $"{fileKey}.${{shortdate}}.{extension}");

        var config = new LoggingConfiguration();

        var fileTarget = new FileTarget("pluginHostFile")
        {
            FileName = fileName,
            ArchiveEvery = FileArchivePeriod.Day,
            ArchiveNumbering = ArchiveNumberingMode.Rolling,
            ArchiveAboveSize = options.ArchiveAboveBytes,
            MaxArchiveFiles = Math.Max(1, options.RetentionDays * 4),
            MaxArchiveDays = Math.Max(1, options.RetentionDays),
            ConcurrentWrites = true,
            KeepFileOpen = false
        };

        fileTarget.Layout = CreateLayout(options.Format);
        config.AddTarget(fileTarget);

        var minLevel = ParseLevel(options.MinLevel);
        config.AddRule(minLevel, NLog.LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;

        _logger.Info(CultureInfo.InvariantCulture, "PluginHost logging initialized: dir={0}, key={1}, format={2}, min={3}", directory, fileKey, options.Format, options.MinLevel);
    }

    public void Info(string message) => _logger.Info(message);

    public void Warn(string message) => _logger.Warn(message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.Error(message);
            return;
        }

        _logger.Error(exception, message);
    }

    public void LogException(Exception exception, string context)
    {
        try
        {
            _logger.Error(exception, "{0}: {1}", context, exception.Message);
        }
        catch
        {
            // best-effort
        }
    }

    private static Layout CreateLayout(string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonLayout
            {
                Attributes =
                {
                    new JsonAttribute("timestamp", "${longdate}"),
                    new JsonAttribute("level", "${level}"),
                    new JsonAttribute("logger", "${logger}"),
                    new JsonAttribute("message", "${message}"),
                    new JsonAttribute("exception", "${exception:format=ToString}"),
                    new JsonAttribute("pid", "${processid}")
                }
            };
        }

        return "${longdate}|${level:uppercase=true}|${message} ${exception:format=Message}";
    }

    private static NLog.LogLevel ParseLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return NLog.LogLevel.Info;
        }

        try
        {
            return NLog.LogLevel.FromString(level.Trim());
        }
        catch
        {
            return NLog.LogLevel.Info;
        }
    }

    private static string ResolveDefaultDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                return Path.Combine(xdgDataHome, "ComCross", "logs", "plugin-host");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "ComCross", "logs", "plugin-host");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComCross",
            "logs",
            "plugin-host");
    }

    private static string SanitizeFileComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var span = value.Trim();
        var buffer = new char[span.Length];
        var j = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (invalid.Contains(ch))
            {
                buffer[j++] = '_';
                continue;
            }

            buffer[j++] = ch;
        }

        return new string(buffer, 0, j);
    }
}
