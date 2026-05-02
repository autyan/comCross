using System;
using System.Collections.Generic;
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
    private readonly PluginManagementService _plugins;
    private readonly IItemVmFactory<PluginItemViewModel, PluginItemContext> _itemFactory;
    private string _pluginsDirectory;

    public event EventHandler? PluginsReloaded;

    public PluginManagerViewModel(
        ILocalizationService localization,
        PluginManagementService plugins,
        IItemVmFactory<PluginItemViewModel, PluginItemContext> itemFactory)
        : base(localization)
    {
        _plugins = plugins;
        _itemFactory = itemFactory;
        _pluginsDirectory = _plugins.RuntimePluginsDirectory;

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
        
        var runtimes = _plugins.GetAllRuntimes();

        foreach (var runtime in runtimes)
        {
            var id = runtime.Info.Manifest.Id;
            var isEnabled = _plugins.IsPluginEnabled(id);
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
            await _plugins.ShutdownAsync();
        }
        catch
        {
            // best-effort
        }

        foreach (var runtime in _plugins.GetAllRuntimes())
        {
            runtime.DisposeHost();
        }
    }

    /// <summary>
    /// 获取特定插件的运行时
    /// </summary>
    public PluginRuntime? GetRuntime(string pluginId) => _plugins.GetRuntime(pluginId);

    public IReadOnlyList<PluginRuntime> GetAllRuntimes() => _plugins.GetAllRuntimes();

    public async Task ToggleAsync(PluginItemViewModel plugin)
    {
        var result = await _plugins.SetPluginEnabledAsync(plugin.Id, plugin.IsEnabled);
        if (!result.Success)
        {
            await LoadAsync();

            var message = result.FailureReason switch
            {
                PluginToggleFailureReason.ActiveSessions => L["settings.plugins.toggle.error.activeSessions"],
                _ => L["settings.plugins.toggle.error.reloadFailed"]
            };

            await MessageBoxService.ShowWarningAsync(
                L["settings.plugins.toggle.title"],
                message);
            return;
        }

        await LoadAsync();
    }

    

    public async Task TestConnectAsync(PluginItemViewModel plugin)
    {
        var runtime = _plugins.GetRuntime(plugin.Id);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(
                L["settings.plugins.connectTest.title"],
                L["settings.plugins.connectTest.error.runtimeNotFound"]);
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(
                L["settings.plugins.connectTest.title"],
                L["settings.plugins.connectTest.error.pluginNotLoaded"]);
            return;
        }

        var capability = runtime.Capabilities.FirstOrDefault();
        if (capability is null)
        {
            await MessageBoxService.ShowWarningAsync(
                L["settings.plugins.connectTest.title"],
                L["settings.plugins.connectTest.error.noCapabilities"]);
            return;
        }

        var parameters = CreateParametersFromDefault(capability.DefaultParametersJson);
        var result = await _plugins.ConnectAsync(
            runtime,
            capability.Id,
            parameters,
            TimeSpan.FromSeconds(3));

        if (result.Ok)
        {
            try
            {
                // Best-effort cleanup: connect test should not leave an open session.
                await _plugins.DisconnectAsync(
                    runtime,
                    sessionId: result.SessionId,
                    reason: "connect-test",
                    timeout: TimeSpan.FromSeconds(3));
            }
            catch
            {
                // best-effort
            }

            var message = string.Format(L["settings.plugins.connectTest.success"], plugin.Name, capability.Id);
            await MessageBoxService.ShowInfoAsync(L["settings.plugins.connectTest.title"], message);
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? L["error.unknown"] : result.Error;
        await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], string.Format(L["settings.plugins.connectTest.failed"], error));
    }

    public async Task TestConnectAsync(PluginItemViewModel plugin, string capabilityId, string? parametersJson)
    {
        var runtime = _plugins.GetRuntime(plugin.Id);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(
                L["settings.plugins.connectTest.title"],
                L["settings.plugins.connectTest.error.runtimeNotFound"]);
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(
                L["settings.plugins.connectTest.title"],
                L["settings.plugins.connectTest.error.pluginNotLoaded"]);
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

        var result = await _plugins.ConnectAsync(
            runtime,
            capabilityId,
            parameters,
            TimeSpan.FromSeconds(3));

        if (result.Ok)
        {
            try
            {
                // Best-effort cleanup: connect test should not leave an open session.
                await _plugins.DisconnectAsync(
                    runtime,
                    sessionId: result.SessionId,
                    reason: "connect-test",
                    timeout: TimeSpan.FromSeconds(3));
            }
            catch
            {
                // best-effort
            }

            var message = string.Format(L["settings.plugins.connectTest.success"], plugin.Name, capabilityId);
            await MessageBoxService.ShowInfoAsync(L["settings.plugins.connectTest.title"], message);
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? L["error.unknown"] : result.Error;
        await MessageBoxService.ShowErrorAsync(L["settings.plugins.connectTest.title"], string.Format(L["settings.plugins.connectTest.failed"], error));
    }

    public IReadOnlyList<CapabilityOption> GetCapabilityOptions(string pluginId)
    {
        var runtime = _plugins.GetRuntime(pluginId);
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
        return _plugins.GetAllRuntimes()
            .Where(r => r.State == PluginLoadState.Loaded)
            .SelectMany(r => r.Capabilities.Select(c => new PluginCapabilityLaunchOption(
                r.Info.Manifest.Id,
                LocalizeOrFallback($"{r.Info.Manifest.Id}.name", r.Info.Manifest.Name),
                c.Id,
                LocalizeOrFallback($"{r.Info.Manifest.Id}.capability.{c.Id}.name", c.Name),
                LocalizeOrFallbackNullable($"{r.Info.Manifest.Id}.capability.{c.Id}.description", c.Description),
                c.Icon,
                c.DefaultParametersJson,
                c.ConnectionResource,
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
        string? viewKind,
        string? viewInstanceId,
        string? resourceKind,
        string? resourceId,
        TimeSpan timeout)
    {
        try
        {
            var runtime = _plugins.GetRuntime(pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                return null;
            }

            var (ok, _, snapshot) = await _plugins.GetUiStateAsync(
                runtime,
                capabilityId,
                sessionId,
                viewKind,
                viewInstanceId,
                resourceKind,
                resourceId,
                timeout);

            return ok && snapshot is not null ? snapshot.State : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Ok, string? Error)> ExecutePluginActionAsync(
        string pluginId,
        string? sessionId,
        string actionName,
        object? parameters,
        TimeSpan? timeout = null)
    {
        try
        {
            var runtime = _plugins.GetRuntime(pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                return (false, Localization.GetString("settings.plugins.connectTest.error.pluginRuntimeNotLoaded"));
            }

            JsonElement? payload = parameters is null
                ? null
                : JsonSerializer.SerializeToElement(parameters);

            var (ok, error, _) = await _plugins.ExecuteActionAsync(
                runtime,
                actionName,
                sessionId,
                payload,
                timeout ?? TimeSpan.FromSeconds(5));

            return (ok, error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public object? TryGetSettingsFieldDefaultValue(string pluginId, string pageId, string fieldKey)
    {
        try
        {
            var runtime = _plugins.GetRuntime(pluginId);
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
            var runtime = _plugins.GetRuntime(pluginId);
            if (runtime is null || runtime.State != PluginLoadState.Loaded)
            {
                // If runtime isn't available, we cannot validate; be conservative and reject.
                error = L["settings.plugins.connectTest.error.pluginRuntimeNotLoaded"];
                return false;
            }

            var capability = runtime.Capabilities.FirstOrDefault(c => string.Equals(c.Id, capabilityId, StringComparison.Ordinal));
            if (capability is null)
            {
                error = L["settings.plugins.connectTest.error.capabilityNotFound"];
                return false;
            }

            if (string.IsNullOrWhiteSpace(capability.JsonSchema))
            {
                // No schema declared => accept as opaque payload.
                return true;
            }

            if (!JsonSchemaLiteValidator.TryParseSchema(capability.JsonSchema, out var schema, out var parseError))
            {
                error = Localization.GetString("settings.plugins.connectTest.error.invalidSchema", parseError ?? string.Empty);
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
        var runtime = _plugins.GetRuntime(pluginId);
        if (runtime is null)
        {
            await MessageBoxService.ShowErrorAsync(
                L["dialog.connect.plugin.title"],
                L["settings.plugins.connectTest.error.runtimeNotFound"]);
            return;
        }

        if (runtime.State != PluginLoadState.Loaded)
        {
            await MessageBoxService.ShowWarningAsync(
                L["dialog.connect.plugin.title"],
                L["settings.plugins.connectTest.error.pluginNotLoaded"]);
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

        var result = await _plugins.ConnectAsync(runtime, capabilityId, parameters, TimeSpan.FromSeconds(3));
        if (result.Ok)
        {
            await MessageBoxService.ShowInfoAsync(
                L["dialog.connect.plugin.title"],
                string.Format(L["dialog.connect.plugin.success"], runtime.Info.Manifest.Name, capabilityId));
            return;
        }

        var error = string.IsNullOrWhiteSpace(result.Error) ? L["error.unknown"] : result.Error;
        await MessageBoxService.ShowErrorAsync(
            L["dialog.connect.plugin.title"],
            string.Format(L["dialog.connect.plugin.failed"], error));
    }

    public async Task<string?> TryGetSuggestedParametersJsonAsync(
        string pluginId,
        string capabilityId,
        string? sessionId,
        string? viewKind,
        string? viewInstanceId,
        TimeSpan timeout)
    {
        var runtime = _plugins.GetRuntime(pluginId);
        if (runtime is null || runtime.State != PluginLoadState.Loaded)
        {
            return null;
        }

        var result = await _plugins.GetUiStateAsync(
            runtime,
            capabilityId,
            sessionId,
            viewKind,
            viewInstanceId,
            resourceKind: null,
            resourceId: null,
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
        await _plugins.NotifyPluginsAsync(notification, onError);
        RefreshRuntimeStates();
    }

    private void RefreshRuntimeStates()
    {
        foreach (var plugin in Plugins)
        {
            var runtime = _plugins.GetRuntime(plugin.Id);
            if (runtime != null)
            {
                plugin.UpdateState(runtime);
            }
        }
    }
}
