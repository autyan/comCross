using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Models;

namespace ComCross.Shell.Services;

/// <summary>
/// Host-side serial ports orchestration for plugin UI.
/// Shared by sidebar and connect-dialog.
/// </summary>
public sealed class SerialPortsHostService
{
    public const string SerialPluginId = "serial.adapter";
    public const string SerialCapabilityId = "serial";
    public const string RefreshPortsHostAction = "comcross.serial.refreshPorts";

    private const string BusAdapterViewKind = "bus-adapter";
    private static readonly PluginUiViewScope BusAdapterScope = new(BusAdapterViewKind);
    private static readonly PluginUiViewScope SettingsScope = new("settings");

    private readonly PluginUiStateManager _stateManager;
    private readonly PluginUiConfigService _pluginUiConfigService;
    private readonly PluginManagerService _pluginManagerService;

    public SerialPortsHostService(
        PluginUiStateManager stateManager,
        PluginUiConfigService pluginUiConfigService,
        PluginManagerService pluginManagerService)
    {
        _stateManager = stateManager;
        _pluginUiConfigService = pluginUiConfigService;
        _pluginManagerService = pluginManagerService;
    }

    public bool IsSerial(string pluginId, string capabilityId)
        => string.Equals(pluginId, SerialPluginId, StringComparison.Ordinal)
           && string.Equals(capabilityId, SerialCapabilityId, StringComparison.Ordinal);

    public async Task RefreshPortsAsync(string pluginId, string? sessionId)
    {
        if (!string.Equals(pluginId, SerialPluginId, StringComparison.Ordinal))
        {
            return;
        }

        var scanPatterns = await TryGetScanPatternsAsync(pluginId);
        var ports = SerialPortScanHelper.GetPorts(scanPatterns).ToList();

        // Keep committed port selectable even if the scan list temporarily misses it.
        var currentState = _stateManager.GetState(BusAdapterScope, sessionId);
        var committedPort = TryGetCommittedPort(currentState);
        if (!string.IsNullOrWhiteSpace(committedPort)
            && !ports.Contains(committedPort, StringComparer.Ordinal))
        {
            ports.Insert(0, committedPort);
        }

        var portsUpdate = new Dictionary<string, object>
        {
            ["ports"] = ports
        };

        _stateManager.UpdateStates(BusAdapterScope, sessionId, portsUpdate);

        // If port is unset, prefer committed defaultParameters.port, otherwise pick the first scanned port.
        EnsurePortDefault(BusAdapterScope, sessionId, ports, committedPort);
    }

    private void EnsurePortDefault(
        PluginUiViewScope viewScope,
        string? sessionId,
        IReadOnlyList<string> ports,
        string? committedPort)
    {
        var current = _stateManager.GetState(viewScope, sessionId);
        if (!current.TryGetValue("port", out var portObj) || string.IsNullOrWhiteSpace(portObj?.ToString()))
        {
            var chosen = !string.IsNullOrWhiteSpace(committedPort)
                ? committedPort
                : ports.Count > 0
                    ? ports[0]
                    : null;

            if (string.IsNullOrWhiteSpace(chosen))
            {
                return;
            }

            _stateManager.UpdateStates(viewScope, sessionId, new Dictionary<string, object>
            {
                ["port"] = chosen!
            });
        }
    }

    private static string? TryGetCommittedPort(IDictionary<string, object> state)
    {
        if (TryGetPath(state, "defaultParameters.port", out var v) && v is not null)
        {
            var unwrapped = UnwrapJsonScalar(v);
            var s = unwrapped?.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        return null;
    }

    public void ApplySchemaDefaults(string? sessionId, PluginUiSchema schema)
    {
        if (schema.Fields is null || schema.Fields.Count == 0)
        {
            return;
        }

        ApplySchemaDefaults(BusAdapterScope, sessionId, schema);
    }

    private void ApplySchemaDefaults(PluginUiViewScope viewScope, string? sessionId, PluginUiSchema schema)
    {
        var state = _stateManager.GetState(viewScope, sessionId);
        var updates = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            if (state.TryGetValue(field.Key, out var existing) && !IsNullOrBlank(existing))
            {
                continue;
            }

            // Prefer projecting from DefaultStatePath (e.g. defaultParameters.baudRate)
            if (!string.IsNullOrWhiteSpace(field.DefaultStatePath)
                && TryGetPath(state, field.DefaultStatePath!, out var projected)
                && !IsNullOrBlank(projected))
            {
                var projectedValue = UnwrapJsonScalar(projected);
                if (projectedValue is not null)
                {
                    updates[field.Key] = projectedValue;
                }
                continue;
            }

            // Fallback to schema DefaultValue
            if (field.DefaultValue is not null)
            {
                var defaultValue = UnwrapJsonScalar(field.DefaultValue);
                if (defaultValue is not null)
                {
                    updates[field.Key] = defaultValue;
                }
            }
        }

        if (updates.Count > 0)
        {
            _stateManager.UpdateStates(viewScope, sessionId, updates);
        }
    }

    public async Task InitializeSerialUiAsync(string pluginId, string capabilityId, string? sessionId, PluginUiSchema schema)
    {
        if (!IsSerial(pluginId, capabilityId))
        {
            return;
        }

        // Apply defaults once (so selects show an initial selection), then refresh ports.
        ApplySchemaDefaults(sessionId, schema);
        await RefreshPortsAsync(pluginId, sessionId);
        ApplySchemaDefaults(sessionId, schema);
    }

    private async Task<string?> TryGetScanPatternsAsync(string pluginId)
    {
        var persisted = await _pluginUiConfigService.TryLoadAsync(
            pluginId,
            capabilityId: "settings:serial-scan",
            sessionId: null,
            viewKind: SettingsScope.ViewKind);

        if (persisted is not null && persisted.TryGetValue("scanPatterns", out var v) && v is not null)
        {
            return v.ToString();
        }

        return TryGetSettingsFieldDefaultValue(pluginId, pageId: "serial-scan", fieldKey: "scanPatterns")?.ToString();
    }

    private object? TryGetSettingsFieldDefaultValue(string pluginId, string pageId, string fieldKey)
    {
        try
        {
            var runtime = _pluginManagerService.GetRuntime(pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                return null;
            }

            var pages = runtime.Info.Manifest.SettingsPages;
            if (pages is null || pages.Count == 0)
            {
                return null;
            }

            var page = pages.FirstOrDefault(p => string.Equals(p.Id, pageId, StringComparison.Ordinal));
            if (page?.UiSchema is null)
            {
                return null;
            }

            var schema = PluginUiSchema.TryParse(page.UiSchema.Value.GetRawText());
            var field = schema?.Fields?.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal));
            return field?.DefaultValue;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNullOrBlank(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is string s)
        {
            return string.IsNullOrWhiteSpace(s);
        }

        return false;
    }

    private static object? UnwrapJsonScalar(object? value)
    {
        if (value is null) return null;
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => je.GetString() ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => je.TryGetInt64(out var i64) ? i64 : je.TryGetDouble(out var d) ? d : je.ToString(),
                System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => je.GetBoolean(),
                _ => je
            };
        }

        return value;
    }

    private static bool TryGetPath(IDictionary<string, object> root, string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path)) return false;

        object? current = root;
        foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(seg, out current)) return false;
                continue;
            }

            if (current is IDictionary<string, object> idict)
            {
                if (!idict.TryGetValue(seg, out current)) return false;
                continue;
            }

            if (current is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (!je.TryGetProperty(seg, out var child)) return false;
                current = child;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }
}
