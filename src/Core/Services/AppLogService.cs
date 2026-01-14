using System.Globalization;
using System.IO;
using ComCross.Shared.Models;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace ComCross.Core.Services;

public sealed class AppLogService
{
    private readonly Logger _logger = LogManager.GetLogger("ComCross.App");
    private AppLogSettings _settings = new();
    private bool _initialized;

    public void Initialize(AppLogSettings settings)
    {
        _settings = settings;
        ConfigureLogging(settings);
        _initialized = true;
    }

    public void Update(AppLogSettings settings)
    {
        _settings = settings;
        if (_initialized)
        {
            ConfigureLogging(settings);
        }
    }

    public void Info(string message)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        _logger.Info(message);
    }

    public void Warn(string message)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        _logger.Warn(message);
    }

    public void Error(string message, Exception? exception = null)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        _logger.Error(exception, message);
    }

    public void LogException(Exception exception, string context)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        if (exception is AppException appException)
        {
            _logger.Error(exception, "{0} [{1}]: {2}", context, appException.Category, appException.Message);
            return;
        }

        _logger.Error(exception, "{0}: {1}", context, exception.Message);
    }

    private static void ConfigureLogging(AppLogSettings settings)
    {
        var directory = ResolveDirectory(settings);
        Directory.CreateDirectory(directory);

        var config = new LoggingConfiguration();
        var fileTarget = new FileTarget("appFile")
        {
            FileName = Path.Combine(directory, "app.log"),
            ArchiveNumbering = ArchiveNumberingMode.Rolling,
            ArchiveAboveSize = 10 * 1024 * 1024,
            MaxArchiveFiles = 10,
            ConcurrentWrites = true,
            KeepFileOpen = false
        };

        fileTarget.Layout = CreateLayout(settings.Format);
        config.AddTarget(fileTarget);

        var minLevel = ParseLevel(settings.MinLevel);
        config.AddRule(minLevel, NLog.LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;
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
                    new JsonAttribute("exception", "${exception:format=ToString}")
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

        return NLog.LogLevel.FromString(level.Trim());
    }

    private static string ResolveDirectory(AppLogSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Directory))
        {
            return settings.Directory;
        }

        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross",
            "app-logs"
        );
        settings.Directory = baseDirectory;
        return baseDirectory;
    }
}
