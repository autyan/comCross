using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk.UI;
using System.Text.Json;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

public sealed class PluginManagerViewModel : BaseViewModel
{
    private const string SegmentUpgradeBytesEnvVar = "COMCROSS_DEV_SEGMENT_UPGRADE_BYTES";

    private readonly PluginDiscoveryService _discoveryService;
    private readonly PluginRuntimeService _runtimeService;
    private readonly PluginManagerService _pluginManagerService;
    private readonly PluginHostProtocolService _protocolService;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly IItemVmFactory<PluginItemViewModel, PluginItemContext> _itemFactory;
    private string _pluginsDirectory;

    public event EventHandler? PluginsReloaded;

    public PluginManagerViewModel(
        ILocalizationService localization,
        PluginDiscoveryService discoveryService,
        PluginRuntimeService runtimeService,
        PluginManagerService pluginManagerService,
        PluginHostProtocolService protocolService,
        SettingsService settingsService,
        NotificationService notificationService,
        IItemVmFactory<PluginItemViewModel, PluginItemContext> itemFactory)
        : base(localization)
    {
        _discoveryService = discoveryService;
        _runtimeService = runtimeService;
        _pluginManagerService = pluginManagerService;
        _protocolService = protocolService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _itemFactory = itemFactory;
        _pluginsDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");

        Plugins = new ItemVmCollection<PluginItemViewModel, PluginItemContext>(_itemFactory);

        // 构造时自动加载当前运行时的状态
        _ = LoadAsync();
    }

    public ItemVmCollection<PluginItemViewModel, PluginItemContext> Plugins { get; }

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
        Plugins.Clear();
        
        var runtimes = _pluginManagerService.GetAllRuntimes();

        foreach (var runtime in runtimes)
        {
            var id = runtime.Info.Manifest.Id;
            var isEnabled = _settingsService.Current.Plugins.Enabled.TryGetValue(id, out var enabled) ? enabled : true;
            Plugins.Add(new PluginItemContext(runtime, isEnabled));
        }

        PluginsReloaded?.Invoke(this, EventArgs.Empty);

        await Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Plugins.Dispose();
        }

        base.Dispose(disposing);
    }

    public async Task ShutdownAllAsync(TimeSpan timeoutPerHost)
    {
        try
        {
            await _pluginManagerService.ShutdownAsync();
        }
        catch
        {
            // best-effort
        }

        foreach (var runtime in _pluginManagerService.GetAllRuntimes())
        {
            runtime.DisposeHost();
        }
    }

    /// <summary>
    /// 获取特定插件的运行时
    /// </summary>
    public PluginRuntime? GetRuntime(string pluginId) => _pluginManagerService.GetRuntime(pluginId);

    public IReadOnlyList<PluginRuntime> GetAllRuntimes() => _pluginManagerService.GetAllRuntimes();

    /// <summary>
    /// 向插件发送请求
    /// </summary>
    public async Task<PluginHostResponse?> SendRequestAsync(string pluginId, PluginHostRequest request)
    {
        var runtime = GetRuntime(pluginId);
        if (runtime?.Client == null)
        {
            return null;
        }

        return await runtime.Client.SendAsync(request, TimeSpan.FromSeconds(5));
    }

    public async Task ToggleAsync(PluginItemViewModel plugin)
    {
        _settingsService.Current.Plugins.Enabled[plugin.Id] = plugin.IsEnabled;
        await _settingsService.SaveAsync();
        await LoadAsync();
    }

    

    public async Task TestConnectAsync(PluginItemViewModel plugin)
    {
        var runtime = _pluginManagerService.GetRuntime(plugin.Id);
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
        var runtime = _pluginManagerService.GetRuntime(plugin.Id);
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
        var runtime = _pluginManagerService.GetRuntime(pluginId);
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
        return _pluginManagerService.GetAllRuntimes()
            .Where(r => r.State == PluginLoadState.Loaded)
            .SelectMany(r => r.Capabilities.Select(c => new PluginCapabilityLaunchOption(
                r.Info.Manifest.Id,
                LocalizeOrFallback($"{r.Info.Manifest.Id}.name", r.Info.Manifest.Name),
                c.Id,
                LocalizeOrFallback($"{r.Info.Manifest.Id}.capability.{c.Id}.name", c.Name),
                LocalizeOrFallbackNullable($"{r.Info.Manifest.Id}.capability.{c.Id}.description", c.Description),
                c.DefaultParametersJson,
                c.JsonSchema,
                c.UiSchema)))
            .ToList();
    }

    private string LocalizeOrFallback(string key, string fallback)
    {
        var localized = L[key];
        return string.Equals(localized, $"[{key}]", StringComparison.Ordinal) ? fallback : localized;
    }

    private string? LocalizeOrFallbackNullable(string key, string? fallback)
    {
        var localized = L[key];
        return string.Equals(localized, $"[{key}]", StringComparison.Ordinal) ? fallback : localized;
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
            var runtime = _pluginManagerService.GetRuntime(pluginId);
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

    public object? TryGetSettingsFieldDefaultValue(string pluginId, string pageId, string fieldKey)
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
            if (schema?.Fields is null)
            {
                return null;
            }

            var field = schema.Fields.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal));
            return field?.DefaultValue;
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
            var runtime = _pluginManagerService.GetRuntime(pluginId);
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
        var runtime = _pluginManagerService.GetRuntime(pluginId);
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
        var runtime = _pluginManagerService.GetRuntime(pluginId);
        if (runtime is null || runtime.State != PluginLoadState.Loaded)
        {
            return null;
        }

        var result = await _protocolService.GetUiStateAsync(
            runtime,
            capabilityId,
            sessionId,
            viewId,
            timeout);

        if (!result.Ok || result.Snapshot is null)
        {
            return null;
        }

        var snapshot = result.Snapshot;

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
        await _runtimeService.NotifyAsync(_pluginManagerService.GetAllRuntimes().ToList(), notification, onError);
        RefreshRuntimeStates();
    }

    private void RefreshRuntimeStates()
    {
        foreach (var plugin in Plugins)
        {
            var runtime = _pluginManagerService.GetRuntime(plugin.Id);
            if (runtime != null)
            {
                plugin.UpdateState(runtime);
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

public sealed record PluginItemContext(
    PluginRuntime Runtime,
    bool IsEnabled);

public sealed class PluginItemViewModel : LocalizedItemViewModelBase<PluginItemContext>
{
    private bool _isEnabled;
    private PluginLoadState _state;
    private int _capabilityCount;
    private bool _capabilitiesError;
    private string _name;

    private string _id = string.Empty;
    private string _version = string.Empty;
    private string _permissions = string.Empty;
    private string _assemblyPath = string.Empty;

    public PluginItemViewModel(ILocalizationService localization)
        : base(localization)
    {
        _name = string.Empty;
    }

    protected override void OnInit(PluginItemContext context)
    {
        _id = context.Runtime.Info.Manifest.Id;
        _name = context.Runtime.Info.Manifest.Name;
        _version = context.Runtime.Info.Manifest.Version;
        _permissions = string.Join(", ", context.Runtime.Info.Manifest.Permissions);
        _assemblyPath = context.Runtime.Info.AssemblyPath;
        _isEnabled = context.IsEnabled;
        _state = context.Runtime.State;

        UpdateState(context.Runtime);
    }

    public string Id => _id;
    public string Name => _name;

    public string DisplayName
    {
        get
        {
            var key = Id + ".name";
            var localized = L[key];
            return string.Equals(localized, $"[{key}]", StringComparison.Ordinal) ? Name : localized;
        }
    }
    public string Version => _version;
    public string Permissions => _permissions;
    public string AssemblyPath => _assemblyPath;
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
        get => State switch
        {
            PluginLoadState.Loaded => L["settings.plugins.status.loaded"],
            PluginLoadState.Disabled => L["settings.plugins.status.disabled"],
            PluginLoadState.Failed => L["settings.plugins.status.failed"],
            _ => L["settings.plugins.status.failed"]
        };
    }

    public string CapabilitiesText
    {
        get
        {
            if (State != PluginLoadState.Loaded)
            {
                return string.Empty;
            }

            return _capabilitiesError
                ? string.Format(L["settings.plugins.capabilities.error"], _capabilityCount)
                : string.Format(L["settings.plugins.capabilities"], _capabilityCount);
        }
    }

    public bool CanConnect
    {
        get => State == PluginLoadState.Loaded && _capabilityCount > 0;
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

    public void UpdateState(PluginRuntime runtime)
    {
        State = runtime.State;

        _name = runtime.Info.Manifest.Name;
        _capabilityCount = runtime.Capabilities?.Count ?? 0;
        _capabilitiesError = !string.IsNullOrWhiteSpace(runtime.CapabilitiesError);

        // Ensure dependents refresh.
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CapabilitiesText));
        OnPropertyChanged(nameof(CanConnect));
    }
}
