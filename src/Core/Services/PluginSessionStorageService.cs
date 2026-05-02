using System.Text.Json;
using ComCross.PluginSdk;

namespace ComCross.Core.Services;

public sealed class PluginSessionStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PluginSessionStorageService(ConfigService configService)
    {
        ArgumentNullException.ThrowIfNull(configService);
        _rootDirectory = Path.Combine(configService.ConfigDirectory, "plugin-session-storage");
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<PluginSessionStorageSnapshot> LoadAsync(
        string pluginId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var filePath = GetFilePath(pluginId, sessionId);
            if (!File.Exists(filePath))
            {
                return new PluginSessionStorageSnapshot(0, new Dictionary<string, JsonElement>());
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var state = JsonSerializer.Deserialize<PluginSessionStorageFile>(json, JsonOptions)
                        ?? new PluginSessionStorageFile();
            return new PluginSessionStorageSnapshot(
                state.SchemaVersion,
                state.Values ?? new Dictionary<string, JsonElement>());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyPatchAsync(
        string pluginId,
        string sessionId,
        PluginSessionStoragePatch? patch,
        CancellationToken cancellationToken = default)
    {
        if (patch is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var filePath = GetFilePath(pluginId, sessionId);
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);

            PluginSessionStorageFile state;
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                state = JsonSerializer.Deserialize<PluginSessionStorageFile>(json, JsonOptions)
                        ?? new PluginSessionStorageFile();
            }
            else
            {
                state = new PluginSessionStorageFile();
            }

            state.Values ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (patch.Deletes is not null)
            {
                foreach (var key in patch.Deletes.Where(k => !string.IsNullOrWhiteSpace(k)))
                {
                    state.Values.Remove(key);
                }
            }

            if (patch.Upserts is not null)
            {
                foreach (var kvp in patch.Upserts.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)))
                {
                    state.Values[kvp.Key] = kvp.Value.Clone();
                }
            }

            if (patch.SchemaVersion is { } schemaVersion)
            {
                state.SchemaVersion = schemaVersion;
            }

            var tempPath = filePath + ".tmp";
            var output = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(tempPath, output, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string pluginId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var filePath = GetFilePath(pluginId, sessionId);
            var tempPath = filePath + ".tmp";

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetFilePath(string pluginId, string sessionId)
        => Path.Combine(_rootDirectory, Sanitize(pluginId), "sessions", Sanitize(sessionId) + ".json");

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));

    private sealed class PluginSessionStorageFile
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, JsonElement>? Values { get; set; } = new(StringComparer.Ordinal);
    }
}
