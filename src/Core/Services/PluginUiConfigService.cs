using System.Text.Json;
using ComCross.PluginSdk.UI;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Host-side persistence for plugin UI configuration (defaults/last-used values).
/// Stored in a single JSON file under the app config directory.
/// </summary>
public sealed class PluginUiConfigService
{
    private readonly ConfigService _configService;
    private readonly PluginUiStateManager _stateManager;
    private readonly ILogger<PluginUiConfigService> _logger;

    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string FileName = "plugin-ui-config.json";

    private Dictionary<string, PluginUiConfigRecord> _records = new(StringComparer.Ordinal);
    private bool _loaded;

    // Debounce saves per record key
    private readonly Dictionary<string, CancellationTokenSource> _saveDebounce = new(StringComparer.Ordinal);

    public PluginUiConfigService(
        ConfigService configService,
        PluginUiStateManager stateManager,
        ILogger<PluginUiConfigService> logger)
    {
        _configService = configService;
        _stateManager = stateManager;
        _logger = logger;

        _stateManager.UiStateChanged += OnUiStateChanged;
    }

    public async Task<Dictionary<string, object>?> TryLoadAsync(
        string pluginId,
        string capabilityId,
        string? sessionId,
        string? viewId,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var key = MakeRecordKey(pluginId, capabilityId, sessionId, viewId);
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return null;
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in record.Values)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }
    }

    /// <summary>
    /// Seed the PluginUiStateManager for a given (plugin, capability, session, view) scope.
    /// 
    /// Merge rule:
    /// - If host has a persisted record: apply persisted values (ignore defaults).
    /// - If host has no record: apply plugin defaults (schema DefaultValue / DefaultStatePath) but do not overwrite existing state.
    /// </summary>
    public async Task SeedStateAsync(
        string pluginId,
        string capabilityId,
        PluginUiSchema schema,
        string? sessionId,
        string? viewId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await TryLoadAsync(pluginId, capabilityId, sessionId, viewId, cancellationToken);
        if (persisted is not null)
        {
            _stateManager.MergeState(sessionId, persisted);
            return;
        }

        // No record: apply defaults.
        var currentState = _stateManager.GetState(sessionId);
        var defaults = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            if (currentState.ContainsKey(field.Key))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(field.DefaultStatePath)
                && currentState.TryGetValue(field.DefaultStatePath!, out var stateDefault)
                && stateDefault is not null)
            {
                defaults[field.Key] = stateDefault;
                continue;
            }

            if (field.DefaultValue is not null)
            {
                defaults[field.Key] = field.DefaultValue;
            }
        }

        if (defaults.Count > 0)
        {
            _stateManager.MergeState(sessionId, defaults);
        }
    }

    public async Task SaveAsync(
        string pluginId,
        string capabilityId,
        string? sessionId,
        string? viewId,
        IDictionary<string, object> values,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var key = MakeRecordKey(pluginId, capabilityId, sessionId, viewId);

        lock (_gate)
        {
            var record = new PluginUiConfigRecord
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            };

            foreach (var kvp in values)
            {
                record.Values[kvp.Key] = ToJsonElement(kvp.Value);
            }

            _records[key] = record;
        }

        await SaveFileAsync(cancellationToken);
    }

    private async void OnUiStateChanged(object? sender, PluginUiStateChangedEvent e)
    {
        // Persist only "default" (no-session) config.
        if (!string.IsNullOrWhiteSpace(e.SessionId))
        {
            return;
        }

        // Avoid persisting noisy UI-state surfaces unless they are user settings/connect forms.
        // - settings:* are plugin settings pages
        // - connect-dialog is where users pick connection defaults
        var isSettings = e.CapabilityId.StartsWith("settings:", StringComparison.Ordinal);
        var isConnect = string.Equals(e.ViewId, "connect-dialog", StringComparison.Ordinal);
        var isSidebarConfig = string.Equals(e.ViewId, "sidebar-config", StringComparison.Ordinal);
        if (!isSettings && !isConnect && !isSidebarConfig)
        {
            return;
        }

        try
        {
            await EnsureLoadedAsync();

            var recordKey = MakeRecordKey(e.PluginId, e.CapabilityId, e.SessionId, e.ViewId);

            lock (_gate)
            {
                if (!_records.TryGetValue(recordKey, out var record))
                {
                    record = new PluginUiConfigRecord
                    {
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    };
                    _records[recordKey] = record;
                }

                record.UpdatedAtUtc = DateTimeOffset.UtcNow;

                if (e.Value is null)
                {
                    record.Values.Remove(e.Key);
                }
                else
                {
                    record.Values[e.Key] = ToJsonElement(e.Value);
                }
            }

            DebouncedSave(recordKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist plugin UI config");
        }
    }

    private void DebouncedSave(string recordKey)
    {
        CancellationTokenSource cts;

        lock (_gate)
        {
            if (_saveDebounce.TryGetValue(recordKey, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            cts = new CancellationTokenSource();
            _saveDebounce[recordKey] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await SaveFileAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Debounced save failed");
            }
        });
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }
        }

        var filePath = Path.Combine(_configService.ConfigDirectory, FileName);
        if (!File.Exists(filePath))
        {
            lock (_gate)
            {
                _records = new Dictionary<string, PluginUiConfigRecord>(StringComparer.Ordinal);
                _loaded = true;
            }
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = JsonSerializer.Deserialize<PluginUiConfigDocument>(json, _jsonOptions) ?? new PluginUiConfigDocument();
            lock (_gate)
            {
                _records = doc.Records ?? new Dictionary<string, PluginUiConfigRecord>(StringComparer.Ordinal);
                _loaded = true;
            }
        }
        catch
        {
            lock (_gate)
            {
                _records = new Dictionary<string, PluginUiConfigRecord>(StringComparer.Ordinal);
                _loaded = true;
            }
        }
    }

    private async Task SaveFileAsync(CancellationToken cancellationToken = default)
    {
        PluginUiConfigDocument snapshot;

        lock (_gate)
        {
            snapshot = new PluginUiConfigDocument
            {
                Records = new Dictionary<string, PluginUiConfigRecord>(_records, StringComparer.Ordinal)
            };
        }

        var filePath = Path.Combine(_configService.ConfigDirectory, FileName);
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static string MakeRecordKey(string pluginId, string capabilityId, string? sessionId, string? viewId)
        => $"{pluginId}::{capabilityId}::{(sessionId ?? "__default__")}::{(viewId ?? "default")}";

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is null)
        {
            return JsonSerializer.SerializeToElement<object?>(null);
        }

        if (value is JsonElement je)
        {
            return je;
        }

        return JsonSerializer.SerializeToElement(value, value.GetType());
    }

    private sealed class PluginUiConfigDocument
    {
        public Dictionary<string, PluginUiConfigRecord>? Records { get; set; }
    }

    private sealed class PluginUiConfigRecord
    {
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.Ordinal);
    }
}
