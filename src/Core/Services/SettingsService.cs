using System.IO;
using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class SettingsService
{
    private readonly ConfigService _configService;
    private readonly AppDatabase _database;
    private AppSettings _current = new();

    public SettingsService(ConfigService configService, AppDatabase database)
    {
        _configService = configService;
        _database = database;
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
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComCross"
        );

        if (string.IsNullOrWhiteSpace(_current.Logs.Directory))
        {
            _current.Logs.Directory = Path.Combine(baseDirectory, "logs");
        }

        if (string.IsNullOrWhiteSpace(_current.Export.DefaultDirectory))
        {
            _current.Export.DefaultDirectory = Path.Combine(baseDirectory, "exports");
        }

        Directory.CreateDirectory(_current.Logs.Directory);
        Directory.CreateDirectory(_current.Export.DefaultDirectory);
    }
}
