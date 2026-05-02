using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Configuration service for persisting workspace state
/// </summary>
public sealed class ConfigService
{
    private readonly string _configDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigService(ComCrossPathService paths)
        : this(paths.ConfigDirectory)
    {
    }

    public ConfigService(string? configDirectory = null)
    {
        _configDirectory = configDirectory ?? new ComCrossPathService().ConfigDirectory;

        Directory.CreateDirectory(_configDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public string ConfigDirectory => _configDirectory;

    public async Task SaveWorkspaceStateAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var filePath = Path.Combine(_configDirectory, "workspace-state.json");
        var tempPath = filePath + ".tmp";
        var backupPath = filePath + ".bak";
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);

        if (File.Exists(filePath))
        {
            try
            {
                File.Copy(filePath, backupPath, overwrite: true);
            }
            catch
            {
                // Backup is best-effort; the temp file remains the source of truth for this write.
            }
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    public async Task<WorkspaceState?> LoadWorkspaceStateAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_configDirectory, "workspace-state.json");
        var tempPath = filePath + ".tmp";
        var backupPath = filePath + ".bak";

        if (!File.Exists(filePath))
        {
            return await TryLoadWorkspaceStateFileAsync(tempPath, cancellationToken)
                ?? await TryLoadWorkspaceStateFileAsync(backupPath, cancellationToken);
        }

        return await TryLoadWorkspaceStateFileAsync(filePath, cancellationToken)
            ?? await TryLoadWorkspaceStateFileAsync(tempPath, cancellationToken)
            ?? await TryLoadWorkspaceStateFileAsync(backupPath, cancellationToken);
    }

    private async Task<WorkspaceState?> TryLoadWorkspaceStateFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<WorkspaceState>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ConfigService: Failed to load workspace state from {filePath}: {ex.Message}");
            return null;
        }
    }

    public async Task SaveToolsetAsync(Toolset toolset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolset);

        var filePath = Path.Combine(_configDirectory, "toolset.json");
        var json = JsonSerializer.Serialize(toolset, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task<Toolset?> LoadToolsetAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_configDirectory, "toolset.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<Toolset>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ConfigService: Failed to load toolset: {ex.Message}");
            return null;
        }
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var filePath = Path.Combine(_configDirectory, "app-settings.json");
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task<AppSettings?> LoadAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_configDirectory, "app-settings.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ConfigService: Failed to load app settings: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Toolset configuration
/// </summary>
public sealed class Toolset
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Default Toolset";
    public List<string> EnabledTools { get; set; } = new();
    public LayoutConfiguration? Layout { get; set; }
    public Dictionary<string, Dictionary<string, object>> ToolSettings { get; set; } = new();
}

public sealed class LayoutConfiguration
{
    public List<PanelConfiguration> Panels { get; set; } = new();
}

public sealed class PanelConfiguration
{
    public string ToolId { get; set; } = string.Empty;
    public string Dock { get; set; } = "right";
    public int Width { get; set; } = 320;
    public bool IsOpen { get; set; }
}
