using System.IO;
using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class SettingsService
{
    private readonly ConfigService _configService;
    private readonly AppDatabase _database;
    private readonly ComCrossPathService _paths;
    private AppSettings _current = new();

    public SettingsService(ConfigService configService, AppDatabase database)
        : this(configService, database, new ComCrossPathService(AppContext.BaseDirectory, configService.ConfigDirectory))
    {
    }

    public SettingsService(ConfigService configService, AppDatabase database, ComCrossPathService paths)
    {
        _configService = configService;
        _database = database;
        _paths = paths;
    }

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public void NotifyChanged()
    {
        SettingsChanged?.Invoke(this, _current);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _configService.LoadAppSettingsAsync(cancellationToken);
        if (settings != null)
        {
            _current = settings;
        }

        MigrateLegacySettings();
        EnsureDefaults();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _configService.SaveAppSettingsAsync(_current, cancellationToken);
        await _database.InsertConfigHistoryAsync(JsonSerializer.Serialize(_current), cancellationToken);
        NotifyChanged();
    }

    private void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(_current.AppLogs.Directory))
        {
            _current.AppLogs.Directory = _paths.AppLogDirectory;
        }

        if (string.IsNullOrWhiteSpace(_current.Export.DefaultDirectory))
        {
            _current.Export.DefaultDirectory = _paths.ExportDirectory;
        }

        if (string.IsNullOrWhiteSpace(_current.Display.UiFontFamily))
        {
            _current.Display.UiFontFamily = DisplaySettings.GetDefaultUiFontFamily();
        }

        if (string.IsNullOrWhiteSpace(_current.Display.FontFamily))
        {
            _current.Display.FontFamily = DisplaySettings.GetDefaultMessageFontFamily();
        }

        if (_current.Display.FontSize <= 0)
        {
            _current.Display.FontSize = 13;
        }

        Directory.CreateDirectory(_current.AppLogs.Directory);
        Directory.CreateDirectory(_current.Export.DefaultDirectory);
    }

    private void MigrateLegacySettings()
    {
        var legacyLogs = _current.Logs;
        if (legacyLogs is null)
        {
            return;
        }

        _current.SessionStorage.GlobalSizeLimitMb = NormalizeMegabytes(legacyLogs.MaxTotalSizeMb, _current.SessionStorage.GlobalSizeLimitMb);
        _current.SessionStorage.PerSessionSizeLimitMb = NormalizeMegabytes(legacyLogs.MaxPerSessionSizeMb, _current.SessionStorage.PerSessionSizeLimitMb);
        _current.SessionStorage.SegmentSizeLimitMb = NormalizeMegabytes(legacyLogs.MaxFileSizeMb, _current.SessionStorage.SegmentSizeLimitMb);
        _current.Logs = null;
    }

    private static int NormalizeMegabytes(int value, int fallback)
        => value > 0 ? value : Math.Max(1, fallback);
}
