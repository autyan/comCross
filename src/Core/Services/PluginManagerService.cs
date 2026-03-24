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

    public async Task<PluginToggleResult> SetPluginEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return new(false, PluginToggleFailureReason.NotFound);
        }

        if (!enabled && _sessionHostRuntimeService.HasActiveSessionsForPlugin(pluginId))
        {
            return new(false, PluginToggleFailureReason.ActiveSessions);
        }

        _settingsService.Current.Plugins.Enabled[pluginId] = enabled;
        await _settingsService.SaveAsync(cancellationToken);
        await ReloadPluginsAsync(cancellationToken);
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
        await _runtimeService.ShutdownAsync(_activeRuntimes.Values.ToList(), TimeSpan.FromSeconds(2));
        await _extensionRuntimeService.ShutdownAsync(TimeSpan.FromSeconds(2));
        _activeRuntimes.Clear();
        _knownRuntimes.Clear();
    }

    public async Task ReloadPluginsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
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

        await _runtimeService.ShutdownAsync(
            _activeRuntimes.Values
                .Where(runtime => runtime.Info.Manifest.PluginType == PluginType.BusAdapter)
                .ToList(),
            TimeSpan.FromSeconds(2));
        await _extensionRuntimeService.ShutdownAsync(TimeSpan.FromSeconds(2));

        _knownRuntimes.Clear();
        _activeRuntimes.Clear();

        var runtimes = _runtimeService.LoadPlugins(busPlugins, _settingsService.Current.Plugins.Enabled)
            .Concat(_extensionRuntimeService.LoadPlugins(extensionPlugins, _settingsService.Current.Plugins.Enabled))
            .ToList();

        _logger.LogInformation("Loaded {LoadedCount}/{TotalCount} plugin runtime(s)",
            runtimes.Count(r => r.State == PluginLoadState.Loaded),
            runtimes.Count);

#if DEBUG
        try
        {
            foreach (var runtime in runtimes)
            {
                Console.Error.WriteLine($"[Core] Plugin runtime: {runtime.Info.Manifest.Id}, State={runtime.State}, Error={runtime.Error ?? "-"}, Capabilities={runtime.Capabilities.Count}, CapErr={runtime.CapabilitiesError ?? "-"}");
            }
        }
        catch
        {
        }
#endif

        foreach (var runtime in runtimes)
        {
            _knownRuntimes[runtime.Info.Manifest.Id] = runtime;
            if (runtime.State == PluginLoadState.Loaded)
            {
                _activeRuntimes[runtime.Info.Manifest.Id] = runtime;
            }
        }
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

public enum PluginToggleFailureReason
{
    ActiveSessions,
    NotFound
}

public sealed record PluginToggleResult(
    bool Success,
    PluginToggleFailureReason? FailureReason);
