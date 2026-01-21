using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shell.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    public const string BusAdapterViewKind = "bus-adapter";

    private const string PluginAdapterPrefix = "plugin:";
    private const string SerialPluginId = SerialPortsHostService.SerialPluginId;
    private const string SerialCapabilityId = SerialPortsHostService.SerialCapabilityId;
    private readonly string _viewKind;
    private readonly string? _viewInstanceId;
    private BusAdapterInfo? _selectedAdapter;
    private BusAdapterListItemViewModel? _selectedAdapterItem;
    private Control? _configPanel; // Changed from UserControl to Control
    private string? _activeSessionId;
    private Session? _activeSession;
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginManagerViewModel _pluginManager;
    private readonly ComCross.Core.Services.PluginUiConfigService _pluginUiConfigService;
    private readonly SerialPortsHostService _serialPorts;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly IItemVmFactory<BusAdapterListItemViewModel, BusAdapterInfo> _adapterItemFactory;
    private readonly ILogger<BusAdapterSelectorViewModel>? _logger;
    private IReadOnlyList<PluginCapabilityLaunchOption> _lastOptions = Array.Empty<PluginCapabilityLaunchOption>();
    private bool _isSyncingSelection;
    private int _panelLoadGeneration;
    private readonly EventHandler<string> _languageChangedHandler;
    private readonly PropertyChangedEventHandler _activeSessionPropertyChangedHandler;

    public BusAdapterSelectorViewModel(
        ILocalizationService localization,
        PluginUiRenderer uiRenderer,
        PluginUiStateManager stateManager,
        PluginManagerViewModel pluginManager,
        ComCross.Core.Services.PluginUiConfigService pluginUiConfigService,
        SerialPortsHostService serialPorts,
        ICapabilityDispatcher dispatcher,
        IWorkspaceCoordinator workspaceCoordinator,
        IItemVmFactory<BusAdapterListItemViewModel, BusAdapterInfo> adapterItemFactory,
        string viewKind = BusAdapterViewKind,
        string? viewInstanceId = null,
        ILogger<BusAdapterSelectorViewModel>? logger = null)
        : base(localization)
    {
        _viewKind = string.IsNullOrWhiteSpace(viewKind) ? BusAdapterViewKind : viewKind;
        _viewInstanceId = string.IsNullOrWhiteSpace(viewInstanceId) ? null : viewInstanceId;
        _uiRenderer = uiRenderer;
        _stateManager = stateManager;
        _pluginManager = pluginManager;
        _pluginUiConfigService = pluginUiConfigService;
        _serialPorts = serialPorts;
        _dispatcher = dispatcher;
        _workspaceCoordinator = workspaceCoordinator;
        _adapterItemFactory = adapterItemFactory;
        _logger = logger;

        AdapterItems = new ItemVmCollection<BusAdapterListItemViewModel, BusAdapterInfo>(_adapterItemFactory);

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect);

        _activeSessionPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(Session.Status) || string.IsNullOrEmpty(args.PropertyName))
            {
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(CanEditConfiguration));
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        };
        
        _languageChangedHandler = (_, _) =>
        {
            if (_lastOptions.Count > 0)
            {
                UpdatePluginAdapters(_lastOptions);
            }

            // Refresh current config panel so plugin UI labels/layout switch with language.
            if (_selectedAdapter?.PluginId != null && _selectedAdapter.CapabilityId != null)
            {
                _uiRenderer.ClearCache(_selectedAdapter.PluginId, _selectedAdapter.CapabilityId, _activeSessionId, _viewKind, _viewInstanceId);
                _ = LoadConfigPanelAsync();
            }
        };

        Localization.LanguageChanged += _languageChangedHandler;
    }

    public ItemVmCollection<BusAdapterListItemViewModel, BusAdapterInfo> AdapterItems { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public bool IsConnected => _activeSession?.Status == SessionStatus.Connected;

    public bool CanEditConfiguration => !IsConnected;

    public bool CanConnect
        => SelectedAdapter?.PluginId != null
           && SelectedAdapter?.CapabilityId != null
           && (SelectedAdapterItem?.IsEnabled ?? false)
           && !IsConnected;

    public bool CanDisconnect
        => _activeSession?.Id is { Length: > 0 }
           && IsConnected;

    public void UpdatePluginAdapters(IReadOnlyList<PluginCapabilityLaunchOption> options)
    {
        _lastOptions = options;

        var previouslySelectedId = SelectedAdapter?.Id;

        AdapterItems.Clear();

        if (options.Count > 0)
        {
            foreach (var option in options)
            {
                var pluginName = TryGetLocalized($"{option.PluginId}.name") ?? option.PluginName;
                var capName = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.name") ?? option.CapabilityName;
                var capDesc = TryGetLocalized($"{option.PluginId}.capability.{option.CapabilityId}.description") ?? option.CapabilityDescription;

                var adapter = new BusAdapterInfo
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
                };

                AdapterItems.Add(adapter);
            }
        }

        // Restore selection if possible
        if (!string.IsNullOrWhiteSpace(previouslySelectedId))
        {
            var match = AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, previouslySelectedId, StringComparison.Ordinal));
            if (match is not null)
            {
                SelectedAdapterItem = match;
                return;
            }
        }

        if (AdapterItems.Count > 0)
        {
            SelectedAdapterItem = AdapterItems[0];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Localization.LanguageChanged -= _languageChangedHandler;
            AdapterItems.Dispose();
        }

        base.Dispose(disposing);
    }

    public void SelectAdapterById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        var adapter = AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, id, StringComparison.Ordinal));
        if (adapter is not null)
        {
            SelectedAdapterItem = adapter;
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

                if (!_isSyncingSelection)
                {
                    _isSyncingSelection = true;
                    try
                    {
                        SelectedAdapterItem = value is null
                            ? null
                            : AdapterItems.FirstOrDefault(a => string.Equals(a.Adapter.Id, value.Id, StringComparison.Ordinal));
                    }
                    finally
                    {
                        _isSyncingSelection = false;
                    }
                }

                _ = LoadConfigPanelAsync();
            }
        }
    }

    public BusAdapterListItemViewModel? SelectedAdapterItem
    {
        get => _selectedAdapterItem;
        set
        {
            if (_selectedAdapterItem != value)
            {
                _selectedAdapterItem = value;
                OnPropertyChanged();

                if (!_isSyncingSelection)
                {
                    _isSyncingSelection = true;
                    try
                    {
                        SelectedAdapter = value?.Adapter;
                    }
                    finally
                    {
                        _isSyncingSelection = false;
                    }
                }
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
        var previousSessionId = _activeSessionId;

        if (_activeSession != null)
        {
            _activeSession.PropertyChanged -= _activeSessionPropertyChangedHandler;
        }

        _activeSession = session;
        _activeSessionId = session?.Id;

        _logger?.LogInformation(
            "BusAdapter VM SetActiveSession: prev={PrevSessionId} next={NextSessionId} adapterId={AdapterId} hasParams={HasParams}",
            previousSessionId,
            _activeSessionId,
            session?.AdapterId,
            !string.IsNullOrWhiteSpace(session?.ParametersJson));

        if (_activeSession != null)
        {
            _activeSession.PropertyChanged += _activeSessionPropertyChangedHandler;
        }

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        
        if (session != null && !string.IsNullOrEmpty(session.AdapterId))
        {
            // Restore adapter selection
            SelectAdapterById(session.AdapterId);

            // If it's a plugin adapter and we have committed parameters, apply them as the authoritative state snapshot.
            // This intentionally discards any draft values (draft persistence is not required).
            if (session.AdapterId.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(session.ParametersJson))
            {
                var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
                var committed = TryDeserializeObject(session.ParametersJson);
                if (committed is not null)
                {
                    _stateManager.SetStateSnapshot(viewScope, session.Id, committed);
                }

                // Diagnostics: capture a representative key after seeding from session.
                var seeded = _stateManager.GetState(viewScope, session.Id);
                seeded.TryGetValue("port", out var seededPort);
                seeded.TryGetValue("ports", out var seededPorts);
                _logger?.LogInformation(
                    "Applied committed parameters to UI state: session={SessionId} keys={KeyCount} port={Port} portType={PortType} portsType={PortsType} portsCount={PortsCount}",
                    session.Id,
                    seeded.Count,
                    seededPort?.ToString(),
                    seededPort?.GetType().FullName,
                    seededPorts?.GetType().FullName,
                    TryGetEnumerableCount(seededPorts));
            }
        }

        // Always reload panel when session changes so controls are registered under the correct session.
        // Otherwise, switching sessions can appear to "stop updating" because controls remain bound
        // to the previous session's registration bucket.
        if (!string.Equals(previousSessionId, _activeSessionId, StringComparison.Ordinal))
        {
            _ = LoadConfigPanelAsync();
        }

        // Apply cached per-session state to any already-rendered controls.
        // (Controls registered after UpdateStates won't receive values until SwitchContext is called.)
        {
            var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
            _stateManager.SwitchContext(viewScope, _activeSessionId);
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

        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Undefined => true,
                System.Text.Json.JsonValueKind.Null => true,
                System.Text.Json.JsonValueKind.String => string.IsNullOrWhiteSpace(je.GetString()),
                _ => false
            };
        }

        return false;
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        if (SelectedAdapter?.PluginId is not { Length: > 0 } pluginId
            || SelectedAdapter.CapabilityId is not { Length: > 0 } capabilityId)
        {
            return;
        }

        // Use the currently bound state bucket (active session if present, otherwise default).
        var stateKey = _activeSessionId;
        var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);
        var currentState = _stateManager.GetState(viewScope, stateKey);

        // Minimal required-field validation (Plugin UI schema v1 uses field.Required).
        var uiSchema = PluginUiSchema.TryParse(SelectedAdapter.UiSchema);
        if (uiSchema?.Fields is { Count: > 0 })
        {
            foreach (var field in uiSchema.Fields)
            {
                if (!field.Required)
                {
                    continue;
                }

                if (!currentState.TryGetValue(field.Key, out var value) || value is null)
                {
                    await MessageBoxService.ShowWarningAsync(
                        Localization.GetString("dialog.connect.title"),
                        string.Format(Localization.GetString("dialog.connect.validation.requiredField"), field.Label ?? field.Key));
                    return;
                }

                if (value is string s && string.IsNullOrWhiteSpace(s))
                {
                    await MessageBoxService.ShowWarningAsync(
                        Localization.GetString("dialog.connect.title"),
                        string.Format(Localization.GetString("dialog.connect.validation.requiredField"), field.Label ?? field.Key));
                    return;
                }
            }
        }

        // If we have a disconnected session selected, reconnect it (reuse its id);
        // otherwise always create a new session.
        var targetSessionId = (_activeSession is not null && _activeSession.Status != SessionStatus.Connected)
            ? _activeSession.Id
            : null;

        // Resource contention rule (UI-host policy): only force disconnect if the *same resource* is already occupied.
        // For Serial, the resource key is the port path/name.
        if (targetSessionId is null
            && string.Equals(pluginId, SerialPluginId, StringComparison.Ordinal)
            && string.Equals(capabilityId, SerialCapabilityId, StringComparison.Ordinal))
        {
            var desiredPort = TryGetStateString(currentState, "port") ?? TryGetStateString(currentState, "Port");
            if (!string.IsNullOrWhiteSpace(desiredPort))
            {
                try
                {
                    var sessions = await _workspaceCoordinator.GetActiveSessionsAsync();
                    var conflict = sessions.FirstOrDefault(s =>
                        s.Status == SessionStatus.Connected
                        && string.Equals(s.PluginId, pluginId, StringComparison.Ordinal)
                        && string.Equals(s.CapabilityId, capabilityId, StringComparison.Ordinal)
                        && string.Equals(TryGetCommittedParameterString(s.ParametersJson, "port"), desiredPort, StringComparison.Ordinal));

                    if (conflict is not null)
                    {
                        var ok = await MessageBoxService.ShowConfirmAsync(
                            Localization.GetString("dialog.connect.title"),
                            $"Port '{desiredPort}' is already in use by session '{conflict.Name}'. Disconnect it and connect the new one?");

                        if (!ok)
                        {
                            return;
                        }

                        await _dispatcher.DispatchAsync(pluginId, conflict.Id, ComCross.Shared.Models.PluginHostMessageTypes.Disconnect, null);
                    }
                }
                catch
                {
                    // Best-effort; if we can't query sessions, just attempt connect.
                }
            }
        }

        var payload = new
        {
            CapabilityId = capabilityId,
            SessionId = targetSessionId,
            Parameters = currentState
        };

        try
        {
            await _dispatcher.DispatchAsync(pluginId, targetSessionId, ComCross.Shared.Models.PluginHostMessageTypes.Connect, payload);
        }
        catch (Exception ex)
        {
            // If the underlying plugin host still enforces single-session, surface a clearer hint.
            if (ex.Message.Contains("Another session is already active", StringComparison.OrdinalIgnoreCase))
            {
                await MessageBoxService.ShowErrorAsync(
                    Localization.GetString("dialog.connect.title"),
                    "The plugin host currently allows only one active session. Disconnect the existing session to connect another. (Multi-session support is pending.)");
                return;
            }

            await MessageBoxService.ShowErrorAsync(
                Localization.GetString("dialog.connect.title"),
                ex.Message);
            return;
        }
    }

    private static string? TryGetStateString(IDictionary<string, object> state, string key)
    {
        if (!state.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return je.GetString();
            }

            return je.ToString();
        }

        return value.ToString();
    }

    private static Dictionary<string, object>? TryDeserializeObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetCommittedParameterString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty(key, out var prop))
            {
                return null;
            }

            return prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : prop.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async System.Threading.Tasks.Task DisconnectAsync()
    {
        if (_activeSession?.Id is not { Length: > 0 } sessionId)
        {
            return;
        }

        if (SelectedAdapter?.PluginId is not { Length: > 0 } pluginId)
        {
            // Dispatcher can resolve pluginId by sessionId, but keeping this conservative.
            pluginId = string.Empty;
        }

        await _dispatcher.DispatchAsync(pluginId, sessionId, ComCross.Shared.Models.PluginHostMessageTypes.Disconnect, null);
    }

    /// <summary>
    /// Load the configuration panel for the selected adapter
    /// </summary>
    private async System.Threading.Tasks.Task LoadConfigPanelAsync()
    {
        var generation = System.Threading.Interlocked.Increment(ref _panelLoadGeneration);
        var expectedSessionId = _activeSessionId;
        var expectedAdapterId = _selectedAdapter?.Id;
        var viewScope = PluginUiViewScope.From(_viewKind, _viewInstanceId);

        bool IsStale()
            => generation != System.Threading.Volatile.Read(ref _panelLoadGeneration)
               || !string.Equals(expectedSessionId, _activeSessionId, StringComparison.Ordinal)
               || !string.Equals(expectedAdapterId, _selectedAdapter?.Id, StringComparison.Ordinal);

        if (_selectedAdapter?.ConfigPanelType != null)
        {
            try
            {
                // Create instance of the config panel
                var panel = Activator.CreateInstance(_selectedAdapter.ConfigPanelType) as Control;
                if (IsStale())
                {
                    return;
                }

                ConfigPanel = panel;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load config panel for {_selectedAdapter.Name}: {ex.Message}");
                if (IsStale())
                {
                    return;
                }

                ConfigPanel = null;
            }
        }
        else if (_selectedAdapter?.PluginId != null)
        {
             // Plugin-backed adapter: Use PluginUiRenderer
             var uiSchema = PluginUiSchema.TryParse(_selectedAdapter.UiSchema);
             var jsonSchema = TryParseJsonSchema(_selectedAdapter.JsonSchema);
             var fields = uiSchema?.Fields;

             var optionKeys = new HashSet<string>(StringComparer.Ordinal);
             if (fields is { Count: > 0 })
             {
                 foreach (var field in fields)
                 {
                     if (!string.IsNullOrWhiteSpace(field.OptionsStatePath))
                     {
                         optionKeys.Add(field.OptionsStatePath);
                     }
                 }
             }

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
                         viewKind: _viewKind,
                         viewInstanceId: _viewInstanceId,
                         timeout: TimeSpan.FromSeconds(2));

                     if (uiState is null && !string.IsNullOrWhiteSpace(_activeSessionId))
                     {
                         uiState = await _pluginManager.TryGetUiStateAsync(
                             _selectedAdapter.PluginId,
                             _selectedAdapter.CapabilityId,
                             sessionId: null,
                             viewKind: _viewKind,
                             viewInstanceId: _viewInstanceId,
                             timeout: TimeSpan.FromSeconds(2));
                     }

                     if (uiState is not null && uiState.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                     {
                         var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(uiState.Value.GetRawText());
                         if (dict != null)
                         {
                             // IMPORTANT: uiState contains dynamic host/UI data (options, defaults, ports, etc.).
                             // It must not overwrite session connection parameters already seeded from Session.ParametersJson.
                             // Merge strategy:
                             // - Always apply option keys (e.g. select options like "ports")
                             // - For all other keys, only fill when the current state is missing/blank
                             var current = _stateManager.GetState(viewScope, _activeSessionId);
                             var toApply = new Dictionary<string, object>(StringComparer.Ordinal);
                             var forced = 0;
                             var filled = 0;
                             var skipped = 0;

                             foreach (var kvp in dict)
                             {
                                 if (string.IsNullOrWhiteSpace(kvp.Key))
                                 {
                                     continue;
                                 }

                                 var forceUpdate = optionKeys.Contains(kvp.Key)
                                                  || string.Equals(kvp.Key, "ports", StringComparison.Ordinal);

                                 if (forceUpdate)
                                 {
                                     toApply[kvp.Key] = kvp.Value;
                                     forced++;
                                     continue;
                                 }

                                 if (!current.TryGetValue(kvp.Key, out var existing) || IsNullOrBlank(existing))
                                 {
                                     toApply[kvp.Key] = kvp.Value;
                                     filled++;
                                 }
                                 else
                                 {
                                     skipped++;
                                 }
                             }

                             if (toApply.Count > 0)
                             {
                                 _stateManager.UpdateStates(viewScope, _activeSessionId, toApply);
                             }

                             _logger?.LogInformation(
                                 "Merged uiState into session state: session={SessionId} total={Total} applied={Applied} forced={Forced} filled={Filled} skipped={Skipped}",
                                 _activeSessionId,
                                 dict.Count,
                                 toApply.Count,
                                 forced,
                                 filled,
                                 skipped);

                             if (fields is { Count: > 0 })
                             {
                                 // Project defaults into field keys, but only when the field value is missing/blank.
                                 var projected = ProjectDefaults(dict, fields);
                                 if (projected.Count > 0)
                                 {
                                     var after = _stateManager.GetState(viewScope, _activeSessionId);
                                     var projectedToApply = new Dictionary<string, object>(StringComparer.Ordinal);
                                     foreach (var kvp in projected)
                                     {
                                         if (!after.TryGetValue(kvp.Key, out var existing) || IsNullOrBlank(existing))
                                         {
                                             projectedToApply[kvp.Key] = kvp.Value;
                                         }
                                     }

                                     if (projectedToApply.Count > 0)
                                     {
                                         _stateManager.UpdateStates(viewScope, _activeSessionId, projectedToApply);
                                     }

                                     _logger?.LogInformation(
                                         "Applied projected defaults: session={SessionId} total={Total} applied={Applied}",
                                         _activeSessionId,
                                         projected.Count,
                                         projectedToApply.Count);
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
             var pluginId = _selectedAdapter.PluginId;
             var capabilityId = _selectedAdapter.CapabilityId;
             if (fields != null && fields.Count > 0 && pluginId != null && capabilityId != null)
             {
                 // Preserve UiSchemaVersion1 layout/title metadata if present.
                 var schema = uiSchema ?? new PluginUiSchema { Fields = fields };
                 schema.Fields = fields;

                 if (_activeSessionId is null)
                 {
                     await _pluginUiConfigService.SeedStateAsync(
                         pluginId,
                         capabilityId,
                         schema,
                         sessionId: null,
                         viewKind: _viewKind,
                         viewInstanceId: _viewInstanceId);
                 }

                 // Host-side serial initialization: apply defaults and load ports once.
                 if (_selectedAdapter.CapabilityId != null
                     && string.Equals(_selectedAdapter.PluginId, SerialPluginId, StringComparison.Ordinal)
                     && string.Equals(_selectedAdapter.CapabilityId, SerialCapabilityId, StringComparison.Ordinal))
                 {
                     try
                     {
                         var before = _stateManager.GetState(viewScope, _activeSessionId);
                         before.TryGetValue("port", out var beforePort);
                         before.TryGetValue("ports", out var beforePorts);
                         _logger?.LogInformation(
                             "Serial UI init (before): session={SessionId} port={Port} portType={PortType} portsType={PortsType} portsCount={PortsCount}",
                             _activeSessionId,
                             beforePort?.ToString(),
                             beforePort?.GetType().FullName,
                             beforePorts?.GetType().FullName,
                             TryGetEnumerableCount(beforePorts));
                     }
                     catch
                     {
                         // best-effort diagnostics
                     }

                     await _serialPorts.InitializeSerialUiAsync(
                         pluginId,
                         capabilityId,
                         _activeSessionId,
                         schema);

                     try
                     {
                         var after = _stateManager.GetState(viewScope, _activeSessionId);
                         after.TryGetValue("port", out var afterPort);
                         after.TryGetValue("ports", out var afterPorts);
                         _logger?.LogInformation(
                             "Serial UI init (after): session={SessionId} port={Port} portType={PortType} portsType={PortsType} portsCount={PortsCount}",
                             _activeSessionId,
                             afterPort?.ToString(),
                             afterPort?.GetType().FullName,
                             afterPorts?.GetType().FullName,
                             TryGetEnumerableCount(afterPorts));
                     }
                     catch
                     {
                         // best-effort diagnostics
                     }
                 }

                 var container = _uiRenderer.GetOrRender(pluginId, capabilityId, schema, _activeSessionId, _viewKind, _viewInstanceId);
                 if (container is AvaloniaPluginUiContainer avaloniaContainer)
                 {
                     if (IsStale())
                     {
                         return;
                     }

                     ConfigPanel = avaloniaContainer.GetPanel();

                     // Ensure newly created/registered controls receive cached state.
                     _stateManager.SwitchContext(viewScope, _activeSessionId);

                     try
                     {
                         var refreshed = _stateManager.GetState(viewScope, _activeSessionId);
                         refreshed.TryGetValue("port", out var refreshedPort);
                         refreshed.TryGetValue("ports", out var refreshedPorts);
                         _logger?.LogInformation(
                             "After SwitchContext: session={SessionId} port={Port} portType={PortType} portsCount={PortsCount}",
                             _activeSessionId,
                             refreshedPort?.ToString(),
                             refreshedPort?.GetType().FullName,
                             TryGetEnumerableCount(refreshedPorts));
                     }
                     catch
                     {
                         // best-effort diagnostics
                     }
                 }
                 else
                 {
                     if (IsStale())
                     {
                         return;
                     }

                     ConfigPanel = null;
                 }
             }
             else
             {
                 if (IsStale())
                 {
                     return;
                 }

                 ConfigPanel = null;
             }
        }
        else
        {
            // No custom config panel, use default or null
            if (IsStale())
            {
                return;
            }

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

    private static int? TryGetEnumerableCount(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is System.Collections.ICollection c)
        {
            return c.Count;
        }

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return je.GetArrayLength();
            }

            return null;
        }

        if (value is System.Collections.IEnumerable e)
        {
            var count = 0;
            foreach (var _ in e)
            {
                count++;
                if (count > 10_000)
                {
                    break;
                }
            }
            return count;
        }

        return null;
    }
}
