using System.Collections.Concurrent;
using ComCross.PluginSdk;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// 管理所有运行中的插件进程实例的任务级服务 (Core 层)
/// </summary>
public sealed class PluginManagerService
{
    private readonly PluginRuntimeService _runtimeService;
    private readonly ExtensionRuntimeService _extensionRuntimeService;
    private readonly SessionHostRuntimeService _sessionHostRuntimeService;
    private readonly PluginDiscoveryService _discoveryService;
    private readonly BundledPluginSynchronizationService _pluginSynchronizationService;
    private readonly ComCrossPathService _paths;
    private readonly PluginUiStateManager _uiStateManager;
    private readonly SettingsService _settingsService;
    private readonly PluginHostEventRouterService _eventRouter;
    private readonly IExtensibleLocalizationService? _extensibleLocalization;
    private readonly ILogger<PluginManagerService> _logger;
    private readonly ConcurrentDictionary<string, byte> _registeredI18n = new();
    private readonly ConcurrentDictionary<string, PluginRuntime> _knownRuntimes = new();
    private readonly ConcurrentDictionary<string, PluginRuntime> _activeRuntimes = new();
    private bool _eventSubscriptionsAttached;

    public PluginManagerService(
        PluginRuntimeService runtimeService,
        ExtensionRuntimeService extensionRuntimeService,
        SessionHostRuntimeService sessionHostRuntimeService,
        PluginDiscoveryService discoveryService,
        BundledPluginSynchronizationService pluginSynchronizationService,
        ComCrossPathService paths,
        PluginUiStateManager uiStateManager,
        SettingsService settingsService,
        PluginHostEventRouterService eventRouter,
        ILocalizationService localization,
        ILogger<PluginManagerService> logger)
    {
        _runtimeService = runtimeService;
        _extensionRuntimeService = extensionRuntimeService;
        _sessionHostRuntimeService = sessionHostRuntimeService;
        _discoveryService = discoveryService;
        _pluginSynchronizationService = pluginSynchronizationService;
        _paths = paths;
        _uiStateManager = uiStateManager;
        _settingsService = settingsService;
        _eventRouter = eventRouter;
        _extensibleLocalization = localization as IExtensibleLocalizationService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing PluginManagerService...");
        EnsureEventSubscriptions();
        await ReloadPluginsAsync();
    }

    private void TryRegisterPluginI18n(IReadOnlyList<PluginInfo> plugins)
    {
        if (_extensibleLocalization is null)
        {
            return;
        }

        foreach (var plugin in plugins)
        {
            if (!_registeredI18n.TryAdd(plugin.Manifest.Id, 0))
            {
                continue;
            }

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

            foreach (var dup in duplicates)
            {
                _logger.LogWarning("Duplicate i18n key ignored: {Key}", dup);
            }

            foreach (var inv in invalid)
            {
                _logger.LogWarning("Invalid i18n key (missing prefix) ignored: {Key}", inv);
            }
        }
    }

    private void HandlePluginEvent(PluginRuntime runtime, PluginHostEvent evt)
    {
        _ = _eventRouter.RouteAsync(runtime, evt);
    }

    private void HandleSessionHostEvent(SessionHostRuntime host, PluginHostEvent evt)
    {
        if (!_activeRuntimes.TryGetValue(host.Plugin.Manifest.Id, out var runtime))
        {
            return;
        }

        _ = _eventRouter.RouteAsync(runtime, evt);
    }

    private void SplitPluginsByPlane(
        IReadOnlyList<PluginInfo> plugins,
        List<PluginInfo> busPlugins,
        List<PluginInfo> extensionPlugins)
    {
        foreach (var plugin in plugins)
        {
            if (!PluginPlaneClassifier.TryClassify(plugin.Manifest, out var plane, out var error))
            {
                _logger.LogError("{Error}", error);
                continue;
            }

            if (plane == PluginPlane.Bus)
            {
                busPlugins.Add(plugin);
            }
            else
            {
                extensionPlugins.Add(plugin);
            }
        }
    }

    public PluginRuntime? GetRuntime(string pluginId)
    {
        return _activeRuntimes.TryGetValue(pluginId, out var runtime) ? runtime : null;
    }

    public IReadOnlyList<PluginRuntime> GetAllRuntimes()
    {
        return _knownRuntimes.Values
            .OrderBy(runtime => runtime.Info.Manifest.Id, StringComparer.Ordinal)
            .ToList();
    }

    public string RuntimePluginsDirectory => _paths.RuntimePluginsDirectory;

    public async Task<PluginToggleResult> SetPluginEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return new(false, PluginToggleFailureReason.NotFound);
        }

        var snapshot = DiscoverPlugins();
        var targetPlugin = snapshot.Plugins.FirstOrDefault(plugin =>
            string.Equals(plugin.Manifest.Id, pluginId, StringComparison.Ordinal));
        if (targetPlugin is null)
        {
            return new(false, PluginToggleFailureReason.NotFound);
        }

        if (!PluginManagerPlaneState.TryGetPlane(targetPlugin.Manifest, out var plane))
        {
            return new(false, PluginToggleFailureReason.NotFound);
        }

        if (!enabled && _sessionHostRuntimeService.HasActiveSessionsForPlugin(pluginId))
        {
            return new(false, PluginToggleFailureReason.ActiveSessions);
        }

        _settingsService.Current.Plugins.Enabled[pluginId] = enabled;
        await _settingsService.SaveAsync(cancellationToken);
        await ReloadPlaneAsync(plane, snapshot, cancellationToken);
        return new(true, null);
    }

    public async Task NotifyPluginsAsync(
        PluginNotification notification,
        Action<PluginRuntime, Exception, bool>? onError = null,
        CancellationToken cancellationToken = default)
    {
        await _runtimeService.NotifyAsync(
            _activeRuntimes.Values
                .Where(runtime => runtime.Info.Manifest.PluginType == PluginType.BusAdapter)
                .ToList(),
            notification,
            onError,
            cancellationToken);

        await _extensionRuntimeService.NotifyAsync(notification, onError, cancellationToken);
    }

    public async Task ShutdownAsync()
    {
        try
        {
            await _runtimeService.ShutdownAsync(
                _activeRuntimes.Values.ToList(),
                TimeSpan.FromSeconds(2),
                (runtime, ex) =>
                {
                    if (ex is null)
                    {
                        return;
                    }

                    _logger.LogWarning(ex, "Bus plugin shutdown reported an error: {PluginId}", runtime.Info.Manifest.Id);
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bus plugin shutdown failed.");
        }

        try
        {
            await _extensionRuntimeService.ShutdownAsync(
                TimeSpan.FromSeconds(2),
                ex =>
                {
                    if (ex is not null)
                    {
                        _logger.LogWarning(ex, "Extension plugin shutdown reported an error.");
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extension plugin shutdown failed.");
        }

        _activeRuntimes.Clear();
        _knownRuntimes.Clear();
    }

    public async Task ReloadPluginsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = DiscoverPlugins();
        await ReloadBusPlaneAsync(snapshot, cancellationToken);
        await ReloadExtensionPlaneAsync(snapshot, cancellationToken);
    }

    private PluginDiscoverySnapshot DiscoverPlugins()
    {
        _pluginSynchronizationService.Synchronize();

        var pluginsDir = _paths.RuntimePluginsDirectory;
        var plugins = _discoveryService.Discover(pluginsDir);
        var busPlugins = new List<PluginInfo>();
        var extensionPlugins = new List<PluginInfo>();

        TryRegisterPluginI18n(plugins);
        SplitPluginsByPlane(plugins, busPlugins, extensionPlugins);

        _logger.LogInformation("Discovered {Count} plugin(s) under {PluginsDir}", plugins.Count, pluginsDir);

#if DEBUG
        try
        {
            Console.Error.WriteLine($"[Core] Discovered {plugins.Count} plugin(s) under {pluginsDir}");
        }
        catch
        {
        }
#endif

        return new PluginDiscoverySnapshot(plugins, busPlugins, extensionPlugins);
    }

    private async Task ReloadPlaneAsync(
        PluginPlane plane,
        PluginDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (plane == PluginPlane.Bus)
        {
            await ReloadBusPlaneAsync(snapshot, cancellationToken);
            return;
        }

        await ReloadExtensionPlaneAsync(snapshot, cancellationToken);
    }

    private async Task ReloadBusPlaneAsync(
        PluginDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existingBusRuntimes = _activeRuntimes.Values
            .Where(runtime => PluginManagerPlaneState.TryGetPlane(runtime.Info.Manifest, out var plane) && plane == PluginPlane.Bus)
            .ToList();

        await _runtimeService.ShutdownAsync(existingBusRuntimes, TimeSpan.FromSeconds(2));
        var runtimes = await _runtimeService.LoadPluginsAsync(snapshot.BusPlugins, _settingsService.Current.Plugins.Enabled);
        PluginManagerPlaneState.ReplacePlane(_knownRuntimes, _activeRuntimes, PluginPlane.Bus, runtimes);
        LogPlaneReload(PluginPlane.Bus, runtimes);
    }

    private async Task ReloadExtensionPlaneAsync(
        PluginDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var runtimes = await _extensionRuntimeService.LoadPluginsAsync(snapshot.ExtensionPlugins, _settingsService.Current.Plugins.Enabled);
        PluginManagerPlaneState.ReplacePlane(_knownRuntimes, _activeRuntimes, PluginPlane.Extension, runtimes);
        LogPlaneReload(PluginPlane.Extension, runtimes);
    }

    private void LogPlaneReload(PluginPlane plane, IReadOnlyList<PluginRuntime> runtimes)
    {
        _logger.LogInformation(
            "Reloaded {Plane} plane: Loaded={LoadedCount}, Total={TotalCount}",
            plane,
            runtimes.Count(r => r.State == PluginLoadState.Loaded),
            runtimes.Count);

#if DEBUG
        try
        {
            foreach (var runtime in runtimes)
            {
                Console.Error.WriteLine($"[Core] {plane} runtime: {runtime.Info.Manifest.Id}, State={runtime.State}, Error={runtime.Error ?? "-"}, Capabilities={runtime.Capabilities.Count}, CapErr={runtime.CapabilitiesError ?? "-"}");
            }
        }
        catch
        {
        }
#endif
    }

    private void EnsureEventSubscriptions()
    {
        if (_eventSubscriptionsAttached)
        {
            return;
        }

        _runtimeService.PluginEventReceived += HandlePluginEvent;
        _extensionRuntimeService.HostEventReceived += HandlePluginEvent;
        _sessionHostRuntimeService.HostEventReceived += HandleSessionHostEvent;
        _eventSubscriptionsAttached = true;
    }
}

internal sealed record PluginDiscoverySnapshot(
    IReadOnlyList<PluginInfo> Plugins,
    IReadOnlyList<PluginInfo> BusPlugins,
    IReadOnlyList<PluginInfo> ExtensionPlugins);

public enum PluginToggleFailureReason
{
    ActiveSessions,
    NotFound
}

public sealed record PluginToggleResult(
    bool Success,
    PluginToggleFailureReason? FailureReason);
