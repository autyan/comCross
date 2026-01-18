using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using System.Text.Json;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

public sealed class PluginManagerViewModel : BaseViewModel
{
    private const string SegmentUpgradeBytesEnvVar = "COMCROSS_DEV_SEGMENT_UPGRADE_BYTES";

    private readonly PluginDiscoveryService _discoveryService;
    private readonly PluginRuntimeService _runtimeService;
    private readonly PluginHostProtocolService _protocolService;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly IExtensibleLocalizationService? _extensibleLocalization;
    private string _pluginsDirectory;
    private List<PluginRuntime> _runtimes = new();

    public event EventHandler? PluginsReloaded;

    public PluginManagerViewModel(
        ILocalizationService localization,
        PluginDiscoveryService discoveryService,
        PluginRuntimeService runtimeService,
        PluginHostProtocolService protocolService,
        SettingsService settingsService,
        NotificationService notificationService)
        : base(localization)
    {
        _discoveryService = discoveryService;
        _runtimeService = runtimeService;
        _protocolService = protocolService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _extensibleLocalization = localization as IExtensibleLocalizationService;
        _pluginsDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();

    public string PluginsDirectory
    {
        get => _pluginsDirectory;
        set
        {
            if (_pluginsDirectory == value)
            {
                return;
            }

            _pluginsDirectory = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.DisposeHost();
        }

        _runtimes.Clear();
        Plugins.Clear();
        var items = _discoveryService.Discover(_pluginsDirectory);

        // Register plugin-provided i18n bundles as early as possible.
        TryRegisterPluginI18n(items);

        _runtimes = _runtimeService.LoadPlugins(items, _settingsService.Current.Plugins.Enabled).ToList();

        foreach (var runtime in _runtimes)
        {
            Plugins.Add(new PluginItemViewModel(runtime, _settingsService.Current.Plugins.Enabled, L));
        }

        PluginsReloaded?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public async Task ShutdownAllAsync(TimeSpan timeoutPerHost)
    {
        try
        {
            await _runtimeService.ShutdownAsync(_runtimes, timeoutPerHost);
        }
        catch
        {
            // best-effort
        }

        foreach (var runtime in _runtimes)
        {
            runtime.DisposeHost();
        }
    }

    private void TryRegisterPluginI18n(IReadOnlyList<PluginInfo> plugins)
    {
        if (_extensibleLocalization is null)
        {
            return;
        }

        foreach (var plugin in plugins)
        {
            var bundle = plugin.Manifest.I18n;
            if (bundle is null || bundle.Count == 0)
            {
                continue;
            }

            var bundlesView = bundle.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyDictionary<string, string>)kvp.Value,
                StringComparer.Ordinal);

            var prefix = plugin.Manifest.Id + ".";

            _extensibleLocalization.RegisterTranslations(
                plugin.Manifest.Id,
                bundlesByCulture: bundlesView,
                duplicateKeys: out var duplicates,
                invalidKeys: out var invalid,
                validateKey: key => key.StartsWith(prefix, StringComparison.Ordinal));

            // Notify duplicates (do not overwrite existing values).
            foreach (var dup in duplicates)
            {
                _ = _notificationService.AddAsync(
                    NotificationCategory.System,
                    NotificationLevel.Warning,
                    "notification.plugin.i18n.duplicateKey",
                    new object[] { dup });
            }

            // Notify invalid keys (missing prefix).
            foreach (var inv in invalid)
            {
                _ = _notificationService.AddAsync(
                    NotificationCategory.System,
                    NotificationLevel.Warning,
                    "notification.plugin.i18n.invalidKeyPrefix",
                    new object[] { inv });
            }
        }
    }

    public async Task ToggleAsync(PluginItemViewModel plugin)
    {
        _settingsService.Current.Plugins.Enabled[plugin.Id] = plugin.IsEnabled;
        await _settingsService.SaveAsync();
        await LoadAsync();
    }

    public void RefreshLocalizedText()
    {
        foreach (var plugin in Plugins)
        {
            plugin.RefreshStatus(L);
            plugin.RefreshCapabilitiesText(L);
        }
    }

    public async Task TestConnectAsync(PluginItemViewModel plugin)
    {
        var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == plugin.Id);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], "Runtime not found.");
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(L["settings.plugins.connectTest.title"], "Plugin not loaded.");
            return;
        }

        var capability = runtime.Capabilities.FirstOrDefault();
        if (capability is null)
        {
            await MessageBoxService.ShowWarningAsync(L["settings.plugins.connectTest.title"], "No capabilities.");
            return;
        }

        var parameters = CreateParametersFromDefault(capability.DefaultParametersJson);
        var result = await _protocolService.ConnectAsync(
            runtime,
            capability.Id,
            parameters,
            TimeSpan.FromSeconds(3));

        if (result.Ok)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(result.SessionId)
                    && TryGetSegmentUpgradeBytes(out var requestedBytes)
                    && requestedBytes > 0)
                {
                    var upgrade = await _protocolService.AllocateAndApplySharedMemorySegmentAsync(
                        runtime,
                        result.SessionId,
                        requestedBytes,
                        TimeSpan.FromSeconds(3));

                    if (!upgrade.Ok)
                    {
                        var upgradeError = string.IsNullOrWhiteSpace(upgrade.Error) ? "Unknown error." : upgrade.Error;
                        await MessageBoxService.ShowWarningAsync(
                            L["settings.plugins.connectTest.title"],
                            $"Segment upgrade denied: {upgradeError}");
                    }
                }
            }
            finally
            {
                // Best-effort cleanup: connect test should not leave an open session.
                await _protocolService.DisconnectAsync(
                    runtime,
                    sessionId: result.SessionId,
                    reason: "connect-test",
                    timeout: TimeSpan.FromSeconds(3));
            }

            var message = string.Format(L["settings.plugins.connectTest.success"], plugin.Name, capability.Id);
            await MessageBoxService.ShowInfoAsync(L["settings.plugins.connectTest.title"], message);
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? "Unknown error." : result.Error;
        await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], string.Format(L["settings.plugins.connectTest.failed"], error));
    }

    public async Task TestConnectAsync(PluginItemViewModel plugin, string capabilityId, string? parametersJson)
    {
        var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == plugin.Id);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], "Runtime not found.");
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(L["settings.plugins.connectTest.title"], "Plugin not loaded.");
            return;
        }

        JsonElement parameters;
        try
        {
            parameters = CreateParametersFromJsonText(parametersJson);
        }
        catch (Exception ex)
        {
            await MessageBoxService.ShowErrorAsync(
                L["settings.plugins.connectTest.dialog.title"],
                string.Format(L["settings.plugins.connectTest.dialog.invalidJson"], ex.Message));
            return;
        }

        var result = await _protocolService.ConnectAsync(
            runtime,
            capabilityId,
            parameters,
            TimeSpan.FromSeconds(3));

        if (result.Ok)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(result.SessionId)
                    && TryGetSegmentUpgradeBytes(out var requestedBytes)
                    && requestedBytes > 0)
                {
                    var upgrade = await _protocolService.AllocateAndApplySharedMemorySegmentAsync(
                        runtime,
                        result.SessionId,
                        requestedBytes,
                        TimeSpan.FromSeconds(3));

                    if (!upgrade.Ok)
                    {
                        var upgradeError = string.IsNullOrWhiteSpace(upgrade.Error) ? "Unknown error." : upgrade.Error;
                        await MessageBoxService.ShowWarningAsync(
                            L["settings.plugins.connectTest.title"],
                            $"Segment upgrade denied: {upgradeError}");
                    }
                }
            }
            finally
            {
                // Best-effort cleanup: connect test should not leave an open session.
                await _protocolService.DisconnectAsync(
                    runtime,
                    sessionId: result.SessionId,
                    reason: "connect-test",
                    timeout: TimeSpan.FromSeconds(3));
            }

            var message = string.Format(L["settings.plugins.connectTest.success"], plugin.Name, capabilityId);
            await MessageBoxService.ShowInfoAsync(L["settings.plugins.connectTest.title"], message);
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? "Unknown error." : result.Error;
        await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], string.Format(L["settings.plugins.connectTest.failed"], error));
    }

    private static bool TryGetSegmentUpgradeBytes(out int bytes)
    {
        bytes = 0;

        var value = Environment.GetEnvironmentVariable(SegmentUpgradeBytesEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return int.TryParse(value, out bytes);
    }

    public IReadOnlyList<CapabilityOption> GetCapabilityOptions(string pluginId)
    {
        var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == pluginId);
        if (runtime is null || runtime.State != PluginLoadState.Loaded)
        {
            return Array.Empty<CapabilityOption>();
        }

        return runtime.Capabilities
            .Select(c => new CapabilityOption(c.Id, c.Name, c.Description, c.DefaultParametersJson))
            .ToList();
    }

    public IReadOnlyList<PluginCapabilityLaunchOption> GetAllCapabilityOptions()
    {
        return _runtimes
            .Where(r => r.State == PluginLoadState.Loaded)
            .SelectMany(r => r.Capabilities.Select(c => new PluginCapabilityLaunchOption(
                r.Info.Manifest.Id,
                r.Info.Manifest.Name,
                c.Id,
                c.Name,
                c.Description,
                c.DefaultParametersJson,
                c.JsonSchema,
                c.UiSchema)))
            .ToList();
    }

    public async Task<JsonElement?> TryGetUiStateAsync(
        string pluginId,
        string capabilityId,
        string? sessionId,
        string? viewId,
        TimeSpan timeout)
    {
        try
        {
            var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                return null;
            }

            var (ok, _, snapshot) = await _protocolService.GetUiStateAsync(
                runtime,
                capabilityId,
                sessionId,
                viewId,
                timeout);

            return ok && snapshot is not null ? snapshot.State : null;
        }
        catch
        {
            return null;
        }
    }

    public bool TryValidateParameters(
        string pluginId,
        string capabilityId,
        JsonElement parameters,
        out string? error)
    {
        error = null;

        try
        {
            var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                // If runtime isn't available, we cannot validate; be conservative and reject.
                error = "Plugin runtime not loaded.";
                return false;
            }

            var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
            if (capability is null)
            {
                error = "Capability not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(capability.JsonSchema))
            {
                // No schema declared => accept as opaque payload.
                return true;
            }

            if (!JsonSchemaLiteValidator.TryParseSchema(capability.JsonSchema, out var schema, out var parseError))
            {
                error = $"Invalid schema: {parseError}";
                return false;
            }

            if (!JsonSchemaLiteValidator.TryValidate(schema, parameters, out var validateError))
            {
                error = validateError;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task ConnectByIdsAsync(string pluginId, string capabilityId, string? parametersJson)
    {
        var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == pluginId);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(L["dialog.connect.plugin.title"], "Runtime not found.");
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(L["dialog.connect.plugin.title"], "Plugin not loaded.");
            return;
        }

        JsonElement parameters;
        try
        {
            parameters = CreateParametersFromJsonText(parametersJson);
        }
        catch (Exception ex)
        {
            await MessageBoxService.ShowErrorAsync(
                L["dialog.connect.plugin.title"],
                string.Format(L["dialog.connect.plugin.invalidJson"], ex.Message));
            return;
        }

        var result = await _protocolService.ConnectAsync(runtime, capabilityId, parameters, TimeSpan.FromSeconds(3));
        if (result.Ok)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(result.SessionId))
                {
                    // ADR-010 closure: shared memory is initialized as part of ConnectAsync when capability declares SharedMemoryRequest.
                    // Keep a dev-only override path to force a larger segment for stress testing.
                    if (TryGetSegmentUpgradeBytes(out var overrideBytes) && overrideBytes > 0)
                    {
                        _ = await _protocolService.AllocateAndApplySharedMemorySegmentAsync(
                            runtime,
                            result.SessionId,
                            overrideBytes,
                            TimeSpan.FromSeconds(3));
                    }
                }
            }
            catch
            {
                // best-effort; do not fail user connect on upgrade negotiation.
            }

            await MessageBoxService.ShowInfoAsync(
                L["dialog.connect.plugin.title"],
                string.Format(L["dialog.connect.plugin.success"], runtime.Info.Manifest.Name, capabilityId));
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? "Unknown error." : result.Error;
        await MessageBoxService.ShowErrorAsync(
            L["dialog.connect.plugin.title"],
            string.Format(L["dialog.connect.plugin.failed"], error));
    }

    public async Task<string?> TryGetSuggestedParametersJsonAsync(
        string pluginId,
        string capabilityId,
        string? sessionId,
        string? viewId,
        TimeSpan timeout)
    {
        var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == pluginId);
        if (runtime is null || runtime.State != PluginLoadState.Loaded)
        {
            return null;
        }

        var (ok, _, snapshot) = await _protocolService.GetUiStateAsync(
            runtime,
            capabilityId,
            sessionId,
            viewId,
            timeout);

        if (!ok || snapshot is null)
        {
            return null;
        }

        try
        {
            // Convention-based extraction:
            // - defaultParametersJson: string
            // - defaultParameters: object (serialized to json)
            var state = snapshot.State;
            if (state.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (state.TryGetProperty("defaultParametersJson", out var jsonText) &&
                jsonText.ValueKind == JsonValueKind.String)
            {
                var text = jsonText.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            if (state.TryGetProperty("defaultParameters", out var obj) &&
                obj.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Serialize(obj);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static JsonElement CreateParametersFromDefault(string? defaultParametersJson)
    {
        if (!string.IsNullOrWhiteSpace(defaultParametersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(defaultParametersJson);
                return doc.RootElement.Clone();
            }
            catch
            {
            }
        }

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static JsonElement CreateParametersFromJsonText(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(parametersJson);
        return doc.RootElement.Clone();
    }

    public Task NotifyLanguageChangedAsync(string cultureCode, Action<PluginRuntime, Exception, bool>? onError = null)
        => NotifyPluginsAsync(PluginNotification.LanguageChanged(cultureCode), onError);

    public async Task NotifyPluginsAsync(PluginNotification notification, Action<PluginRuntime, Exception, bool>? onError = null)
    {
        await _runtimeService.NotifyAsync(_runtimes, notification, onError);
        RefreshRuntimeStates();
    }

    private void RefreshRuntimeStates()
    {
        foreach (var plugin in Plugins)
        {
            var runtime = _runtimes.FirstOrDefault(item => item.Info.Manifest.Id == plugin.Id);
            if (runtime != null)
            {
                plugin.UpdateState(runtime, L);
            }
        }
    }
}

public sealed record CapabilityOption(
    string Id,
    string Name,
    string? Description,
    string? DefaultParametersJson);

public sealed record PluginCapabilityLaunchOption(
    string PluginId,
    string PluginName,
    string CapabilityId,
    string CapabilityName,
    string? CapabilityDescription,
    string? DefaultParametersJson,
    string? JsonSchema,
    string? UiSchema);

public sealed class PluginItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _statusText = string.Empty;
    private string _capabilitiesText = string.Empty;
    private bool _canConnect;
    private PluginLoadState _state;

    public PluginItemViewModel(
        PluginRuntime runtime,
        IReadOnlyDictionary<string, bool> enabledMap,
        ILocalizationStrings localizedStrings)
    {
        Id = runtime.Info.Manifest.Id;
        Name = runtime.Info.Manifest.Name;
        Version = runtime.Info.Manifest.Version;
        Permissions = string.Join(", ", runtime.Info.Manifest.Permissions);
        AssemblyPath = runtime.Info.AssemblyPath;
        _isEnabled = enabledMap.TryGetValue(Id, out var isEnabled) ? isEnabled : true;
        _state = runtime.State;
        RefreshStatus(localizedStrings);
        RefreshCapabilities(runtime, localizedStrings);
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Permissions { get; }
    public string AssemblyPath { get; }
    public PluginLoadState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string CapabilitiesText
    {
        get => _capabilitiesText;
        private set
        {
            if (_capabilitiesText == value)
            {
                return;
            }

            _capabilitiesText = value;
            OnPropertyChanged();
        }
    }

    public bool CanConnect
    {
        get => _canConnect;
        private set
        {
            if (_canConnect == value)
            {
                return;
            }

            _canConnect = value;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public void RefreshStatus(ILocalizationStrings localizedStrings)
    {
        StatusText = State switch
        {
            PluginLoadState.Loaded => localizedStrings["settings.plugins.status.loaded"],
            PluginLoadState.Disabled => localizedStrings["settings.plugins.status.disabled"],
            PluginLoadState.Failed => localizedStrings["settings.plugins.status.failed"],
            _ => localizedStrings["settings.plugins.status.failed"]
        };
    }

    public void RefreshCapabilitiesText(ILocalizationStrings localizedStrings)
    {
        // Re-render the already computed stateful string using the latest localization.
        // If we are not loaded, keep it empty.
        if (State != PluginLoadState.Loaded)
        {
            CapabilitiesText = string.Empty;
        }
        else if (CapabilitiesText.StartsWith("Capabilities", StringComparison.OrdinalIgnoreCase) ||
                 CapabilitiesText.StartsWith("能力", StringComparison.OrdinalIgnoreCase))
        {
            // No-op; actual refresh requires runtime counts, handled in UpdateState.
        }
    }

    public void RefreshCapabilities(PluginRuntime runtime, ILocalizationStrings localizedStrings)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            CapabilitiesText = string.Empty;
            CanConnect = false;
            return;
        }

        var count = runtime.Capabilities?.Count ?? 0;
        CanConnect = count > 0;
        if (!string.IsNullOrWhiteSpace(runtime.CapabilitiesError))
        {
            CapabilitiesText = string.Format(
                localizedStrings["settings.plugins.capabilities.error"],
                count);
            return;
        }

        CapabilitiesText = string.Format(localizedStrings["settings.plugins.capabilities"], count);
    }

    public void UpdateState(PluginRuntime runtime, ILocalizationStrings localizedStrings)
    {
        State = runtime.State;
        RefreshStatus(localizedStrings);
        RefreshCapabilities(runtime, localizedStrings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
