using System.Collections.Concurrent;
using System.Text.Json;
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
    private readonly PluginDiscoveryService _discoveryService;
    private readonly PluginUiStateManager _uiStateManager;
    private readonly IExtensibleLocalizationService? _extensibleLocalization;
    private readonly ILogger<PluginManagerService> _logger;
    private readonly ConcurrentDictionary<string, PluginRuntime> _activeRuntimes = new();

    public PluginManagerService(
        PluginRuntimeService runtimeService,
        PluginDiscoveryService discoveryService,
        PluginUiStateManager uiStateManager,
        ILocalizationService localization,
        ILogger<PluginManagerService> logger)
    {
        _runtimeService = runtimeService;
        _discoveryService = discoveryService;
        _uiStateManager = uiStateManager;
        _extensibleLocalization = localization as IExtensibleLocalizationService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing PluginManagerService...");
        
        // Define default plugins directory
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var plugins = _discoveryService.Discover(pluginsDir);

        TryRegisterPluginI18n(plugins);

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
        
        // 注意：这里默认加载所有插件
        var runtimes = _runtimeService.LoadPlugins(plugins, new Dictionary<string, bool>());
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
            if (runtime.State == PluginLoadState.Loaded)
            {
                _activeRuntimes[runtime.Info.Manifest.Id] = runtime;
            }
        }

        _runtimeService.PluginEventReceived += HandlePluginEvent;
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
        if (string.Equals(evt.Type, PluginHostEventTypes.UiStateInvalidated, StringComparison.Ordinal))
        {
            if (evt.Payload is null) return;

            try
            {
                var invalidated = evt.Payload.Value.Deserialize<PluginUiStateInvalidatedEvent>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (invalidated == null) return;

                // 核心不直接驱动 UI 重新获取状态，而是可以通过 EventBus 或其他方式通知
                // 或者在这里触发 re-fetch 并通过 UiStateManager 更新
                _logger.LogInformation("UI State invalidated for {PluginId}, Session {SessionId}", 
                    runtime.Info.Manifest.Id, invalidated.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle UiStateInvalidated from {PluginId}", runtime.Info.Manifest.Id);
            }
        }
    }

    public PluginRuntime? GetRuntime(string pluginId)
    {
        return _activeRuntimes.TryGetValue(pluginId, out var runtime) ? runtime : null;
    }

    public IReadOnlyList<PluginRuntime> GetAllRuntimes()
    {
        return _activeRuntimes.Values.ToList();
    }

    public async Task ShutdownAsync()
    {
        await _runtimeService.ShutdownAsync(_activeRuntimes.Values.ToList(), TimeSpan.FromSeconds(2));
        _activeRuntimes.Clear();
    }
}
