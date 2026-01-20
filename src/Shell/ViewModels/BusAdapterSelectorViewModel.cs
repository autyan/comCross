using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ComCross.Shared.Models;
using ComCross.Shell.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;
using ComCross.Shell.Views;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    private const string PluginAdapterPrefix = "plugin:";
    private BusAdapterInfo? _selectedAdapter;
    private Control? _configPanel; // Changed from UserControl to Control
    private string? _activeSessionId;
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginManagerViewModel _pluginManager;
    private readonly ComCross.Core.Services.PluginUiConfigService _pluginUiConfigService;
    private IReadOnlyList<PluginCapabilityLaunchOption> _lastOptions = Array.Empty<PluginCapabilityLaunchOption>();

    public BusAdapterSelectorViewModel(
        ILocalizationService localization,
        PluginUiRenderer uiRenderer,
        PluginUiStateManager stateManager,
        PluginManagerViewModel pluginManager,
        ComCross.Core.Services.PluginUiConfigService pluginUiConfigService)
        : base(localization)
    {
        _uiRenderer = uiRenderer;
        _stateManager = stateManager;
        _pluginManager = pluginManager;
        _pluginUiConfigService = pluginUiConfigService;
        
        AvailableAdapters = new ObservableCollection<BusAdapterInfo>();

        Localization.LanguageChanged += (_, _) =>
        {
            if (_lastOptions.Count > 0)
            {
                UpdatePluginAdapters(_lastOptions);
            }

            // Refresh current config panel so plugin UI labels/layout switch with language.
            if (_selectedAdapter?.PluginId != null && _selectedAdapter.CapabilityId != null)
            {
                _uiRenderer.ClearCache(_selectedAdapter.PluginId, _selectedAdapter.CapabilityId, _activeSessionId, "sidebar-config");
                _ = LoadConfigPanelAsync();
            }
        };
    }

    /// <summary>
    /// Available bus adapters
    /// </summary>
    public ObservableCollection<BusAdapterInfo> AvailableAdapters { get; }

    public void UpdatePluginAdapters(IReadOnlyList<PluginCapabilityLaunchOption> options)
    {
        _lastOptions = options;

        var previouslySelectedId = SelectedAdapter?.Id;

        AvailableAdapters.Clear();

        if (options.Count > 0)
        {
            foreach (var option in options)
            {
                var pluginName = TryGetLocalized($"{option.PluginId}.name") ?? option.PluginName;
                var capName = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.name") ?? option.CapabilityName;
                var capDesc = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.description") ?? option.CapabilityDescription;

                AvailableAdapters.Add(new BusAdapterInfo
                {
                    Id = $"{PluginAdapterPrefix}{option.PluginId}:{option.CapabilityId}",
                    Name = $"{pluginName} / {capName}",
                    Icon = "🧩",
                    Description = capDesc,
                    IsEnabled = true,
                    ConfigPanelType = null,
                    PluginId = option.PluginId,
                    CapabilityId = option.CapabilityId,
                    DefaultParametersJson = option.DefaultParametersJson,
                    JsonSchema = option.JsonSchema,
                    UiSchema = option.UiSchema
                });
            }
        }

        // Restore selection if possible
        if (!string.IsNullOrWhiteSpace(previouslySelectedId))
        {
            var match = AvailableAdapters.FirstOrDefault(a => string.Equals(a.Id, previouslySelectedId, StringComparison.Ordinal));
            if (match is not null)
            {
                SelectedAdapter = match;
                return;
            }
        }

        if (AvailableAdapters.Count > 0)
        {
            SelectedAdapter = AvailableAdapters[0];
        }
    }

    public void SelectAdapterById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        var adapter = AvailableAdapters.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (adapter != null)
        {
            SelectedAdapter = adapter;
        }
    }

    /// <summary>
    /// Currently selected bus adapter
    /// </summary>
    public BusAdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (_selectedAdapter != value)
            {
                _selectedAdapter = value;
                OnPropertyChanged();
                _ = LoadConfigPanelAsync();
            }
        }
    }

    /// <summary>
    /// Configuration panel for the selected adapter
    /// </summary>
    public Control? ConfigPanel
    {
        get => _configPanel;
        private set
        {
            if (_configPanel != value)
            {
                _configPanel = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetActiveSession(Session? session)
    {
        _activeSessionId = session?.Id;
        
        if (session != null && !string.IsNullOrEmpty(session.AdapterId))
        {
            // Restore adapter selection
            SelectAdapterById(session.AdapterId);

            // If it's a plugin adapter and we have parameters, sync them to StateManager
            if (session.AdapterId.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(session.ParametersJson))
            {
                _stateManager.UpdateSessionState(session.Id, session.ParametersJson);
            }
        }
    }

    /// <summary>
    /// Load the configuration panel for the selected adapter
    /// </summary>
    private async System.Threading.Tasks.Task LoadConfigPanelAsync()
    {
        if (_selectedAdapter?.ConfigPanelType != null)
        {
            try
            {
                // Create instance of the config panel
                var panel = Activator.CreateInstance(_selectedAdapter.ConfigPanelType) as Control;
                ConfigPanel = panel;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load config panel for {_selectedAdapter.Name}: {ex.Message}");
                ConfigPanel = null;
            }
        }
        else if (_selectedAdapter?.PluginId != null)
        {
             // Plugin-backed adapter: Use PluginUiRenderer
             var uiSchema = PluginUiSchema.TryParse(_selectedAdapter.UiSchema);
             var jsonSchema = TryParseJsonSchema(_selectedAdapter.JsonSchema);
             var fields = uiSchema?.Fields;

             if (fields != null && fields.Count > 0 && jsonSchema != null)
             {
                 EnrichFieldsFromJsonSchema(fields, jsonSchema.Value);
             }

             // Pull UI state (ports, defaults, etc.) so select/options populate.
             try
             {
                 if (_selectedAdapter.CapabilityId != null)
                 {
                     var uiState = await _pluginManager.TryGetUiStateAsync(
                         _selectedAdapter.PluginId,
                         _selectedAdapter.CapabilityId,
                         sessionId: _activeSessionId,
                         viewId: "sidebar-config",
                         timeout: TimeSpan.FromSeconds(2));

                     if (uiState is not null && uiState.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                     {
                         var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(uiState.Value.GetRawText());
                         if (dict != null)
                         {
                             _stateManager.UpdateStates(_activeSessionId, dict);
                             if (fields != null && fields.Count > 0)
                             {
                                 var projected = ProjectDefaults(dict, fields);
                                 if (projected.Count > 0)
                                 {
                                     _stateManager.UpdateStates(_activeSessionId, projected);
                                 }
                             }
                         }
                     }
                 }
             }
             catch
             {
                 // best-effort
             }
             
             // If no fields, we don't show a panel (or could fallback to JsonSchema, but keeping it simple for now)
             if (fields != null && fields.Count > 0 && _selectedAdapter.CapabilityId != null)
             {
                 // Preserve UiSchemaVersion1 layout/title metadata if present.
                 var schema = uiSchema ?? new PluginUiSchema { Fields = fields };
                 schema.Fields = fields;

                 if (_activeSessionId is null)
                 {
                     await _pluginUiConfigService.SeedStateAsync(
                         _selectedAdapter.PluginId,
                         _selectedAdapter.CapabilityId,
                         schema,
                         sessionId: null,
                         viewId: "sidebar-config");
                 }

                 var container = _uiRenderer.GetOrRender(_selectedAdapter.PluginId, _selectedAdapter.CapabilityId, schema, _activeSessionId, "sidebar-config");
                 if (container is AvaloniaPluginUiContainer avaloniaContainer)
                 {
                     ConfigPanel = avaloniaContainer.GetPanel();
                 }
                 else
                 {
                     ConfigPanel = null;
                 }
             }
             else
             {
                 ConfigPanel = null;
             }
        }
        else
        {
            // No custom config panel, use default or null
            ConfigPanel = null;
        }
    }

    private string? TryGetLocalized(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var value = Localization.GetString(key);
        if (string.Equals(value, $"[{key}]", StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static System.Text.Json.JsonElement? TryParseJsonSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static void EnrichFieldsFromJsonSchema(List<PluginUiField> fields, System.Text.Json.JsonElement schema)
    {
        // Minimal enum extraction: { properties: { fieldName: { enum: [...] } } }
        if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in fields)
        {
            if (!field.EnumFromSchema || field.GetOptionsAsOptionList().Count > 0)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(field.Name) ? field.Name : field.Key;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!props.TryGetProperty(name, out var propSchema) || propSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            if (!propSchema.TryGetProperty("enum", out var enums) || enums.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            var list = new List<string>();
            foreach (var item in enums.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }
            }

            if (list.Count > 0)
            {
                field.Options = System.Text.Json.JsonSerializer.SerializeToElement(list);
            }
        }
    }

    private static Dictionary<string, object> ProjectDefaults(Dictionary<string, object> uiState, List<PluginUiField> fields)
    {
        var projected = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || string.IsNullOrWhiteSpace(field.DefaultStatePath))
            {
                continue;
            }

            if (TryGetPath(uiState, field.DefaultStatePath!, out var value) && value is not null)
            {
                projected[field.Key] = value;
            }
        }

        return projected;
    }

    private static bool TryGetPath(Dictionary<string, object> root, string path, out object? value)
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
