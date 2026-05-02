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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _configService.LoadAppSettingsAsync(cancellationToken);
        if (settings != null)
        {
            _current = settings;
        }

        EnsureDefaults();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _configService.SaveAppSettingsAsync(_current, cancellationToken);
        await _database.InsertConfigHistoryAsync(JsonSerializer.Serialize(_current), cancellationToken);
        SettingsChanged?.Invoke(this, _current);
    }

    private void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(_current.Logs.Directory))
        {
            _current.Logs.Directory = _paths.LogDirectory;
        }

        if (string.IsNullOrWhiteSpace(_current.AppLogs.Directory))
        {
            _current.AppLogs.Directory = _paths.AppLogDirectory;
        }

        if (string.IsNullOrWhiteSpace(_current.Export.DefaultDirectory))
        {
            _current.Export.DefaultDirectory = _paths.ExportDirectory;
        }

        Directory.CreateDirectory(_current.Logs.Directory);
        Directory.CreateDirectory(_current.AppLogs.Directory);
        Directory.CreateDirectory(_current.Export.DefaultDirectory);
    }
}
