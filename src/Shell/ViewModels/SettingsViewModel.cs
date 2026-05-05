using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia;
using Avalonia.Threading;
using ComCross.Platform;
using ComCross.Core.Services;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;

namespace ComCross.Shell.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;
    private readonly PluginManagerViewModel _pluginManager;
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _pluginUiStateManager;
    private readonly PluginUiConfigService _pluginUiConfigService;
    private readonly ComCrossPathService _paths;
    private readonly IStorageHealthService _storageHealth;
    private readonly IStorageCalibrationService _storageCalibration;
    private readonly List<LocaleCultureInfo> _availableLanguages;
    private readonly List<string> _logFormats = new() { "txt", "json" };
    private readonly List<string> _logLevels = new() { "Trace", "Debug", "Info", "Warn", "Error", "Fatal" };
    private LocaleCultureInfo _selectedLanguage;
    private bool _followSystemLanguage;
    private bool _saveScheduled;

    private IReadOnlyList<PluginSettingsPageOption> _pluginSettingsPages = Array.Empty<PluginSettingsPageOption>();
    private PluginSettingsPageOption? _selectedPluginSettingsPage;
    private Control? _pluginSettingsPanel;

    // Lazy-rendered plugin settings pages: per-culture control cache.
    // NOTE: cache controls, not authoritative state. We re-apply state on swaps.
    private readonly Dictionary<string, Dictionary<(string PluginId, string PageId), Control>> _pluginSettingsPanelsByCulture = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<(string PluginId, string PageId)>> _renderedPluginSettingsPagesByCulture = new(StringComparer.Ordinal);

    private readonly object _pluginSettingsWarmupGate = new();
    private CancellationTokenSource? _pluginSettingsWarmupCts;
    private int _pluginSettingsWarmupGeneration;

    // Final/serialized switching: coalesce bursts of language toggles.
    private CancellationTokenSource? _pluginSettingsWarmupRequestCts;
    private int _pluginSettingsWarmupRequestId;

    private IReadOnlyList<SettingsNavEntry> _navigationEntries = Array.Empty<SettingsNavEntry>();
    private SettingsNavEntry? _selectedNavigationEntry;

    private string _navigationCacheCulture = string.Empty;
    private bool _navigationCacheInvalidated = true;

    public SettingsViewModel(
        ILocalizationService localization,
        SettingsService settingsService,
        PluginManagerViewModel pluginManager,
        PluginUiRenderer uiRenderer,
        PluginUiStateManager pluginUiStateManager,
        PluginUiConfigService pluginUiConfigService,
        ComCrossPathService paths,
        IStorageHealthService storageHealth,
        IStorageCalibrationService storageCalibration)
        : base(localization)
    {
        _settingsService = settingsService;
        _pluginManager = pluginManager;
        _uiRenderer = uiRenderer;
        _pluginUiStateManager = pluginUiStateManager;
        _pluginUiConfigService = pluginUiConfigService;
        _paths = paths;
        _storageHealth = storageHealth;
        _storageCalibration = storageCalibration;
        _availableLanguages = localization.AvailableCultures.ToList();
        _selectedLanguage = ResolveLanguage(settingsService.Current.Language);
        _followSystemLanguage = settingsService.Current.FollowSystemLanguage;

        InvalidateNavigationCache();
        EnsureNavigationCache();

        // Kick off lazy plugin settings rendering as early as possible,
        // but after the constructor returns (avoid crashing Settings page on init).
        RequestPluginSettingsWarmup(resetAllCaches: true);

        _pluginManager.PluginsReloaded += (_, _) =>
        {
            InvalidateNavigationCache();
            EnsureNavigationCache();
            OnPropertyChanged(nameof(PluginSettingsPages));
            OnPropertyChanged(nameof(NavigationEntries));
            OnPropertyChanged(nameof(SelectedNavigationEntry));
            OnPropertyChanged(nameof(IsSystemSettingsSelected));
            OnPropertyChanged(nameof(IsStorageSettingsSelected));
            OnPropertyChanged(nameof(IsPluginSettingsSelected));
            OnPropertyChanged(nameof(IsPluginManagerSelected));

            RequestPluginSettingsWarmup(resetAllCaches: true);
        };

        Localization.LanguageChanged += (_, _) =>
        {
            InvalidateNavigationCache();
            EnsureNavigationCache();
            OnPropertyChanged(nameof(PluginSettingsPages));
            OnPropertyChanged(nameof(NavigationEntries));
            OnPropertyChanged(nameof(SelectedNavigationEntry));
            OnPropertyChanged(nameof(IsSystemSettingsSelected));
            OnPropertyChanged(nameof(IsPluginManagerSelected));
            OnPropertyChanged(nameof(IsStorageSettingsSelected));
            OnPropertyChanged(nameof(IsPluginSettingsSelected));

            // Rebuild plugin settings panels via the same lazy warm-up pipeline.
            RequestPluginSettingsWarmup(resetAllCaches: false);
        };
    }

    private void RequestPluginSettingsWarmup(bool resetAllCaches)
    {
        // Debounce and ensure last request wins.
        _pluginSettingsWarmupRequestCts?.Cancel();
        _pluginSettingsWarmupRequestCts?.Dispose();
        _pluginSettingsWarmupRequestCts = new CancellationTokenSource();
        var cts = _pluginSettingsWarmupRequestCts;
        var requestId = ++_pluginSettingsWarmupRequestId;

        _ = Task.Run(async () =>
        {
            try
            {
                // Coalesce rapid language toggles.
                var delayMs = resetAllCaches ? 0 : 150;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cts.Token);
                }
            }
            catch
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested || requestId != _pluginSettingsWarmupRequestId)
                {
                    return;
                }

                _ = WarmUpPluginSettingsPagesAsync(Localization.CurrentCulture, resetAllCaches);
            });
        });
    }
    public PluginManagerViewModel PluginManager => _pluginManager;
    public bool IsLinux => PlatformInfo.IsLinux;

    public IReadOnlyList<PluginSettingsPageOption> PluginSettingsPages
    {
        get
        {
            EnsureNavigationCache();
            return _pluginSettingsPages;
        }
    }

    public IReadOnlyList<SettingsNavEntry> NavigationEntries
    {
        get
        {
            EnsureNavigationCache();
            return _navigationEntries;
        }
    }

    public SettingsNavEntry? SelectedNavigationEntry
    {
        get
        {
            EnsureNavigationCache();
            return _selectedNavigationEntry;
        }
        set
        {
            if (Equals(_selectedNavigationEntry, value))
            {
                return;
            }

            // Headers are displayed as group separators only.
            if (value is not null && !value.IsSelectable)
            {
                return;
            }

            _selectedNavigationEntry = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemSettingsSelected));
            OnPropertyChanged(nameof(IsStorageSettingsSelected));
            OnPropertyChanged(nameof(IsPluginSettingsSelected));
            OnPropertyChanged(nameof(IsPluginManagerSelected));

            if (_selectedNavigationEntry?.Kind == SettingsNavKind.PluginPage)
            {
                SelectedPluginSettingsPage = new PluginSettingsPageOption(
                    _selectedNavigationEntry.PluginId ?? string.Empty,
                    _selectedNavigationEntry.PageId ?? string.Empty,
                    _selectedNavigationEntry.Title,
                    _selectedNavigationEntry.UiSchemaJson ?? string.Empty);
            }
            else
            {
                SelectedPluginSettingsPage = null;
            }

            if (_selectedNavigationEntry?.Kind == SettingsNavKind.Storage)
            {
                RefreshStorageSummary();
            }
        }
    }

    public bool IsSystemSettingsSelected => _selectedNavigationEntry is null || _selectedNavigationEntry.Kind == SettingsNavKind.System;
    public bool IsStorageSettingsSelected => _selectedNavigationEntry?.Kind == SettingsNavKind.Storage;
    public bool IsPluginManagerSelected => _selectedNavigationEntry?.Kind == SettingsNavKind.PluginManager;
    public bool IsPluginSettingsSelected => _selectedNavigationEntry?.Kind == SettingsNavKind.PluginPage;

    public PluginSettingsPageOption? SelectedPluginSettingsPage
    {
        get => _selectedPluginSettingsPage;
        set
        {
            if (Equals(_selectedPluginSettingsPage, value))
            {
                return;
            }

            _selectedPluginSettingsPage = value;
            OnPropertyChanged();

            if (_selectedPluginSettingsPage is null)
            {
                PluginSettingsPanel = null;
                return;
            }

            var key = (_selectedPluginSettingsPage.PluginId, _selectedPluginSettingsPage.PageId);
            if (TryGetPluginSettingsPanel(Localization.CurrentCulture, key, out var panel))
            {
                PluginSettingsPanel = panel;

                // Ensure final consistency: re-apply current state to the newly shown tree.
                var viewScope = new ComCross.PluginSdk.UI.PluginUiViewScope("settings", Localization.CurrentCulture);
                Dispatcher.UIThread.Post(() => _pluginUiStateManager.SwitchContext(viewScope, sessionId: null));
            }
            else
            {
                // If a page somehow gets selected before it's rendered, show nothing.
                PluginSettingsPanel = null;
            }
        }
    }

    public Control? PluginSettingsPanel
    {
        get => _pluginSettingsPanel;
        private set
        {
            if (Equals(_pluginSettingsPanel, value))
            {
                return;
            }

            _pluginSettingsPanel = value;
            OnPropertyChanged();
        }
    }

    private void InvalidateNavigationCache()
    {
        _navigationCacheInvalidated = true;
    }

    private void EnsureNavigationCache()
    {
        var culture = Localization.CurrentCulture;
        if (!_navigationCacheInvalidated && string.Equals(_navigationCacheCulture, culture, StringComparison.Ordinal))
        {
            return;
        }

        HashSet<(string PluginId, string PageId)> renderedPages;
        lock (_pluginSettingsWarmupGate)
        {
            renderedPages = _renderedPluginSettingsPagesByCulture.TryGetValue(culture, out var set)
                ? new HashSet<(string PluginId, string PageId)>(set)
                : new HashSet<(string PluginId, string PageId)>();
        }

        var previousSelection = _selectedNavigationEntry;

        try
        {
            var pages = new List<PluginSettingsPageOption>();

            var entries = new List<SettingsNavEntry>();
            entries.Add(SettingsNavEntry.Header(L["settings.nav.system"]));
            entries.Add(SettingsNavEntry.SystemPage(L["settings.nav.system.page"]));
            entries.Add(SettingsNavEntry.StoragePage(L["settings.nav.storage.page"]));
            entries.Add(SettingsNavEntry.PluginManagerPage(L["settings.nav.plugins.page"]));

            foreach (var runtime in _pluginManager.GetAllRuntimes())
            {
                var pluginId = runtime.Info.Manifest.Id;
                var pluginName = runtime.Info.Manifest.Name;
                if (string.IsNullOrWhiteSpace(pluginName))
                {
                    pluginName = pluginId;
                }

                // Prefer runtime-provided i18n key: "{pluginId}.name".
                var localizedPluginName = L[$"{pluginId}.name"];
                if (!string.IsNullOrWhiteSpace(localizedPluginName)
                    && !string.Equals(localizedPluginName, $"[{pluginId}.name]", StringComparison.Ordinal))
                {
                    pluginName = localizedPluginName;
                }

                var manifestPages = runtime.Info.Manifest.SettingsPages;
                if (manifestPages is null || manifestPages.Count == 0)
                {
                    continue;
                }

                // Only show this plugin section when at least one of its settings pages is rendered.
                var hasAnyRenderedPage = false;
                foreach (var page in manifestPages)
                {
                    if (string.IsNullOrWhiteSpace(page.Id) || page.UiSchema is null)
                    {
                        continue;
                    }

                    if (renderedPages.Contains((pluginId, page.Id)))
                    {
                        hasAnyRenderedPage = true;
                        break;
                    }
                }

                if (!hasAnyRenderedPage)
                {
                    continue;
                }

                entries.Add(SettingsNavEntry.Header(pluginName));

                foreach (var page in manifestPages)
                {
                    if (string.IsNullOrWhiteSpace(page.Id) || page.UiSchema is null)
                    {
                        continue;
                    }

                    // Hide page until it's rendered.
                    if (!renderedPages.Contains((pluginId, page.Id)))
                    {
                        continue;
                    }

                    var title = !string.IsNullOrWhiteSpace(page.TitleKey) ? L[page.TitleKey] : page.Title;
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = $"{pluginId}:{page.Id}";
                    }

                    var uiSchemaJson = page.UiSchema.Value.GetRawText();
                    pages.Add(new PluginSettingsPageOption(pluginId, page.Id, title, uiSchemaJson));
                    entries.Add(SettingsNavEntry.PluginPage(pluginId, page.Id, title, uiSchemaJson));
                }
            }

            _pluginSettingsPages = pages;
            _navigationEntries = entries;

            // Preserve selection across cache rebuild (titles change with language).
            _selectedNavigationEntry = previousSelection?.Kind switch
            {
                SettingsNavKind.System => entries.FirstOrDefault(e => e.Kind == SettingsNavKind.System && e.IsSelectable),
                SettingsNavKind.Storage => entries.FirstOrDefault(e => e.Kind == SettingsNavKind.Storage && e.IsSelectable),
                SettingsNavKind.PluginManager => entries.FirstOrDefault(e => e.Kind == SettingsNavKind.PluginManager && e.IsSelectable),
                SettingsNavKind.PluginPage => entries.FirstOrDefault(e => e.Kind == SettingsNavKind.PluginPage
                    && e.IsSelectable
                    && string.Equals(e.PluginId, previousSelection.PluginId, StringComparison.Ordinal)
                    && string.Equals(e.PageId, previousSelection.PageId, StringComparison.Ordinal)),
                _ => null
            };

            // Default to System Settings.
            _selectedNavigationEntry ??= entries.FirstOrDefault(e => e.Kind == SettingsNavKind.System && e.IsSelectable);

            // Sync right panel content.
            if (_selectedNavigationEntry?.Kind == SettingsNavKind.PluginPage)
            {
                SelectedPluginSettingsPage = new PluginSettingsPageOption(
                    _selectedNavigationEntry.PluginId ?? string.Empty,
                    _selectedNavigationEntry.PageId ?? string.Empty,
                    _selectedNavigationEntry.Title,
                    _selectedNavigationEntry.UiSchemaJson ?? string.Empty);
            }
            else
            {
                SelectedPluginSettingsPage = null;
            }

            _navigationCacheCulture = culture;
            _navigationCacheInvalidated = false;
        }
        catch
        {
            _pluginSettingsPages = Array.Empty<PluginSettingsPageOption>();
            _navigationEntries = Array.Empty<SettingsNavEntry>();
            _selectedNavigationEntry = null;
            SelectedPluginSettingsPage = null;
            _navigationCacheCulture = culture;
            _navigationCacheInvalidated = false;
        }
    }

    private bool TryGetPluginSettingsPanel(string culture, (string PluginId, string PageId) key, out Control panel)
    {
        lock (_pluginSettingsWarmupGate)
        {
            if (_pluginSettingsPanelsByCulture.TryGetValue(culture, out var panels)
                && panels.TryGetValue(key, out panel!))
            {
                return true;
            }

            panel = null!;
            return false;
        }
    }

    private async Task WarmUpPluginSettingsPagesAsync(string culture, bool resetAllCaches)
    {
        CancellationTokenSource cts;
        int generation;

        lock (_pluginSettingsWarmupGate)
        {
            _pluginSettingsWarmupCts?.Cancel();
            _pluginSettingsWarmupCts?.Dispose();
            _pluginSettingsWarmupCts = new CancellationTokenSource();
            cts = _pluginSettingsWarmupCts;
            generation = ++_pluginSettingsWarmupGeneration;

            if (resetAllCaches)
            {
                // Plugin reload / cold start: discard all cultures.
                _pluginSettingsPanelsByCulture.Clear();
                _renderedPluginSettingsPagesByCulture.Clear();
            }
        }

        try
        {
            // Only proceed if the requested culture is still current.
            if (!string.Equals(Localization.CurrentCulture, culture, StringComparison.Ordinal))
            {
                return;
            }

            // Immediately reflect hidden state in the UI on full reset.
            if (resetAllCaches)
            {
                InvalidateNavigationCache();
                EnsureNavigationCache();
                OnPropertyChanged(nameof(PluginSettingsPages));
                OnPropertyChanged(nameof(NavigationEntries));
                OnPropertyChanged(nameof(SelectedNavigationEntry));
            OnPropertyChanged(nameof(IsPluginSettingsSelected));
            OnPropertyChanged(nameof(IsStorageSettingsSelected));
            }

            // Snapshot pages to render.
            var targets = new List<PluginSettingsPageOption>();
            foreach (var runtime in _pluginManager.GetAllRuntimes())
            {
                var pluginId = runtime.Info.Manifest.Id;
                var manifestPages = runtime.Info.Manifest.SettingsPages;
                if (manifestPages is null || manifestPages.Count == 0)
                {
                    continue;
                }

                foreach (var page in manifestPages)
                {
                    if (string.IsNullOrWhiteSpace(page.Id) || page.UiSchema is null)
                    {
                        continue;
                    }

                    var title = !string.IsNullOrWhiteSpace(page.TitleKey) ? L[page.TitleKey] : page.Title;
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = $"{pluginId}:{page.Id}";
                    }

                    targets.Add(new PluginSettingsPageOption(pluginId, page.Id, title, page.UiSchema.Value.GetRawText()));
                }
            }

            foreach (var target in targets)
            {
                if (cts.IsCancellationRequested || generation != _pluginSettingsWarmupGeneration)
                {
                    return;
                }

                if (!string.Equals(Localization.CurrentCulture, culture, StringComparison.Ordinal))
                {
                    return;
                }

                var schema = PluginUiSchema.TryParse(target.UiSchemaJson);
                if (schema is null)
                {
                    continue;
                }

                var capabilityId = $"settings:{target.PageId}";

                // Skip if already cached for this culture.
                lock (_pluginSettingsWarmupGate)
                {
                    if (_pluginSettingsPanelsByCulture.TryGetValue(culture, out var existingPanels)
                        && existingPanels.ContainsKey((target.PluginId, target.PageId)))
                    {
                        if (!_renderedPluginSettingsPagesByCulture.TryGetValue(culture, out var existingRendered))
                        {
                            existingRendered = new HashSet<(string PluginId, string PageId)>();
                            _renderedPluginSettingsPagesByCulture[culture] = existingRendered;
                        }

                        existingRendered.Add((target.PluginId, target.PageId));
                        continue;
                    }
                }

                // Seed persisted state (I/O) off the UI thread.
                await _pluginUiConfigService.SeedStateAsync(
                    target.PluginId,
                    capabilityId,
                    schema,
                    sessionId: null,
                    viewKind: "settings",
                    viewInstanceId: culture);

                if (cts.IsCancellationRequested || generation != _pluginSettingsWarmupGeneration)
                {
                    return;
                }

                // Render controls on the UI thread.
                var panel = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_uiRenderer is ComCross.Shell.Plugins.UI.AvaloniaPluginUiRenderer avalonia)
                    {
                        return avalonia.RenderNewPanel(target.PluginId, capabilityId, schema, sessionId: null, viewKind: "settings", viewInstanceId: culture);
                    }

                    // Fallback: replace the renderer cache (no multi-culture support).
                    _uiRenderer.ClearCache(target.PluginId, capabilityId, sessionId: null, viewKind: "settings", viewInstanceId: culture);
                    var container = _uiRenderer.GetOrRender(target.PluginId, capabilityId, schema, sessionId: null, viewKind: "settings", viewInstanceId: culture);
                    return container is AvaloniaPluginUiContainer avaloniaContainer ? avaloniaContainer.GetPanel() : null;
                });

                if (panel is null)
                {
                    continue;
                }

                lock (_pluginSettingsWarmupGate)
                {
                    if (cts.IsCancellationRequested || generation != _pluginSettingsWarmupGeneration)
                    {
                        return;
                    }

                    if (!_pluginSettingsPanelsByCulture.TryGetValue(culture, out var panels))
                    {
                        panels = new Dictionary<(string PluginId, string PageId), Control>();
                        _pluginSettingsPanelsByCulture[culture] = panels;
                    }

                    if (!_renderedPluginSettingsPagesByCulture.TryGetValue(culture, out var rendered))
                    {
                        rendered = new HashSet<(string PluginId, string PageId)>();
                        _renderedPluginSettingsPagesByCulture[culture] = rendered;
                    }

                    panels[(target.PluginId, target.PageId)] = panel;
                    rendered.Add((target.PluginId, target.PageId));
                }

                // Make the page visible as soon as it's ready.
                InvalidateNavigationCache();
                EnsureNavigationCache();
                OnPropertyChanged(nameof(PluginSettingsPages));
                OnPropertyChanged(nameof(NavigationEntries));

                if (_selectedPluginSettingsPage is not null
                    && string.Equals(_selectedPluginSettingsPage.PluginId, target.PluginId, StringComparison.Ordinal)
                    && string.Equals(_selectedPluginSettingsPage.PageId, target.PageId, StringComparison.Ordinal))
                {
                    PluginSettingsPanel = panel;
                    var viewScope = new ComCross.PluginSdk.UI.PluginUiViewScope("settings", culture);
                    _pluginUiStateManager.SwitchContext(viewScope, sessionId: null);
                }

                // Yield so we don't block the UI thread with a long chain of renders.
                await Task.Yield();
            }
        }
        catch
        {
            // best-effort warm-up
        }
    }

    public sealed record PluginSettingsPageOption(string PluginId, string PageId, string Title, string UiSchemaJson);

    public enum SettingsNavKind
    {
        System,
        Storage,
        PluginManager,
        PluginPage
    }

    public sealed record SettingsNavEntry(
        SettingsNavKind Kind,
        string Title,
        bool IsSelectable,
        string? PluginId = null,
        string? PageId = null,
        string? UiSchemaJson = null)
    {
        private static readonly IBrush HeaderForeground = new SolidColorBrush(Color.Parse("#87909B"));
        private static readonly IBrush ItemForeground = new SolidColorBrush(Color.Parse("#E6EDF3"));

        public static SettingsNavEntry Header(string title) => new(SettingsNavKind.System, title, IsSelectable: false);
        public static SettingsNavEntry SystemPage(string title) => new(SettingsNavKind.System, title, IsSelectable: true);
        public static SettingsNavEntry StoragePage(string title) => new(SettingsNavKind.Storage, title, IsSelectable: true);
        public static SettingsNavEntry PluginManagerPage(string title) => new(SettingsNavKind.PluginManager, title, IsSelectable: true);

        public static SettingsNavEntry PluginPage(string pluginId, string pageId, string title, string uiSchemaJson)
            => new(SettingsNavKind.PluginPage, title, IsSelectable: true, PluginId: pluginId, PageId: pageId, UiSchemaJson: uiSchemaJson);

        public double TitleFontSize => IsSelectable ? 12 : 11;
        public FontWeight TitleFontWeight => IsSelectable ? FontWeight.Normal : FontWeight.Bold;
        public IBrush TitleForeground => IsSelectable ? ItemForeground : HeaderForeground;
        public Thickness TitleMargin => IsSelectable ? new Thickness(0) : new Thickness(0, 10, 0, 0);
    }

    public IReadOnlyList<LocaleCultureInfo> AvailableLanguages => _availableLanguages;
    public IReadOnlyList<string> AppLogFormats => _logFormats;
    public IReadOnlyList<string> AppLogLevels => _logLevels;

    public string StorageDataDirectory => _paths.LocalDataDirectory;
    public string StorageSpoolDirectory => _paths.SessionSpoolDirectory;
    // i18n-ignore (data format)
    public string StorageCalibrationStatus => $"{_storageCalibration.Current.Phase} / {_storageCalibration.Current.Tier}";
    // i18n-ignore (data format)
    public string StorageHealthStatus => $"{_storageHealth.Current.Health} / {_storageHealth.Current.Tier}";
    public string StorageSpoolUsage => FormatBytes(GetDirectorySize(_paths.SessionSpoolDirectory));

    public int StorageGlobalLimitMb
    {
        get => _settingsService.Current.SessionStorage.GlobalSizeLimitMb;
        set
        {
            var normalized = Math.Max(1, value);
            if (_settingsService.Current.SessionStorage.GlobalSizeLimitMb == normalized)
            {
                return;
            }

            _settingsService.Current.SessionStorage.GlobalSizeLimitMb = normalized;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int StoragePerSessionLimitMb
    {
        get => _settingsService.Current.SessionStorage.PerSessionSizeLimitMb;
        set
        {
            var normalized = Math.Max(1, value);
            if (_settingsService.Current.SessionStorage.PerSessionSizeLimitMb == normalized)
            {
                return;
            }

            _settingsService.Current.SessionStorage.PerSessionSizeLimitMb = normalized;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int StorageSegmentLimitMb
    {
        get => _settingsService.Current.SessionStorage.SegmentSizeLimitMb;
        set
        {
            var normalized = Math.Max(1, value);
            if (_settingsService.Current.SessionStorage.SegmentSizeLimitMb == normalized)
            {
                return;
            }

            _settingsService.Current.SessionStorage.SegmentSizeLimitMb = normalized;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public void RefreshStorageSummary()
    {
        OnPropertyChanged(nameof(StorageDataDirectory));
        OnPropertyChanged(nameof(StorageSpoolDirectory));
        OnPropertyChanged(nameof(StorageCalibrationStatus));
        OnPropertyChanged(nameof(StorageHealthStatus));
        OnPropertyChanged(nameof(StorageSpoolUsage));
        OnPropertyChanged(nameof(StorageGlobalLimitMb));
        OnPropertyChanged(nameof(StoragePerSessionLimitMb));
        OnPropertyChanged(nameof(StorageSegmentLimitMb));
    }

    public void OpenDataDirectory() => OpenDirectory(_paths.LocalDataDirectory);

    public void OpenSessionSpoolDirectory() => OpenDirectory(_paths.SessionSpoolDirectory);

    public LocaleCultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value)
            {
                return;
            }

            // Enforce follow system language priority
            if (FollowSystemLanguage)
            {
                return;
            }

            _selectedLanguage = value;
            _settingsService.Current.Language = value.Code;
            ApplyLanguage(value.Code);
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool FollowSystemLanguage
    {
        get => _followSystemLanguage;
        set
        {
            if (_followSystemLanguage == value)
            {
                return;
            }

            _followSystemLanguage = value;
            _settingsService.Current.FollowSystemLanguage = value;
            if (value)
            {
                ApplySystemLanguage();
            }

            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool AppLogEnabled
    {
        get => _settingsService.Current.AppLogs.Enabled;
        set
        {
            if (_settingsService.Current.AppLogs.Enabled == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Enabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogDirectory
    {
        get => _settingsService.Current.AppLogs.Directory;
        set
        {
            if (_settingsService.Current.AppLogs.Directory == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Directory = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogFormat
    {
        get => _settingsService.Current.AppLogs.Format;
        set
        {
            if (_settingsService.Current.AppLogs.Format == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Format = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogMinLevel
    {
        get => _settingsService.Current.AppLogs.MinLevel;
        set
        {
            if (_settingsService.Current.AppLogs.MinLevel == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.MinLevel = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsStorageAlertsEnabled
    {
        get => _settingsService.Current.Notifications.StorageAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.StorageAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.StorageAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsConnectionAlertsEnabled
    {
        get => _settingsService.Current.Notifications.ConnectionAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.ConnectionAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.ConnectionAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsExportAlertsEnabled
    {
        get => _settingsService.Current.Notifications.ExportAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.ExportAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.ExportAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int NotificationsRetentionDays
    {
        get => _settingsService.Current.Notifications.RetentionDays;
        set
        {
            if (_settingsService.Current.Notifications.RetentionDays == value)
            {
                return;
            }

            _settingsService.Current.Notifications.RetentionDays = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool ConnectionDefaultAddCr
    {
        get => _settingsService.Current.Connection.DefaultAddCr;
        set
        {
            if (_settingsService.Current.Connection.DefaultAddCr == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultAddCr = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool ConnectionDefaultAddLf
    {
        get => _settingsService.Current.Connection.DefaultAddLf;
        set
        {
            if (_settingsService.Current.Connection.DefaultAddLf == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultAddLf = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool DisplayAutoScroll
    {
        get => _settingsService.Current.Display.AutoScroll;
        set
        {
            if (_settingsService.Current.Display.AutoScroll == value)
            {
                return;
            }

            _settingsService.Current.Display.AutoScroll = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string DisplayTimestampFormat
    {
        get => _settingsService.Current.Display.TimestampFormat;
        set
        {
            if (_settingsService.Current.Display.TimestampFormat == value)
            {
                return;
            }

            _settingsService.Current.Display.TimestampFormat = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string DisplayFontFamily
    {
        get => _settingsService.Current.Display.UiFontFamily;
        set
        {
            var normalized = NormalizeFontFamilyList(value, DisplaySettings.GetDefaultUiFontFamily());
            if (_settingsService.Current.Display.UiFontFamily == normalized)
            {
                return;
            }

            _settingsService.Current.Display.UiFontFamily = normalized;
            _settingsService.NotifyChanged();
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string MessageFontFamily
    {
        get => _settingsService.Current.Display.FontFamily;
        set
        {
            var normalized = NormalizeFontFamilyList(value, DisplaySettings.GetDefaultMessageFontFamily());
            if (_settingsService.Current.Display.FontFamily == normalized)
            {
                return;
            }

            _settingsService.Current.Display.FontFamily = normalized;
            _settingsService.NotifyChanged();
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int MessageFontSize
    {
        get => _settingsService.Current.Display.FontSize;
        set
        {
            var normalized = Math.Clamp(value, 8, 24);
            if (_settingsService.Current.Display.FontSize == normalized)
            {
                return;
            }

            _settingsService.Current.Display.FontSize = normalized;
            _settingsService.NotifyChanged();
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    private static string NormalizeFontFamilyList(string? value, string fallback)
    {
        var families = SplitFontFamilyList(value)
            .Select(UnquoteFontFamily)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return families.Length == 0 ? fallback : string.Join(", ", families);
    }

    private static IEnumerable<string> SplitFontFamilyList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        const char apostrophe = (char)39;
        const char quotationMark = (char)34;
        var current = new StringBuilder();
        var quote = '\0';
        foreach (var ch in value)
        {
            if ((ch == apostrophe || ch == quotationMark) && quote == '\0')
            {
                quote = ch;
                current.Append(ch);
                continue;
            }

            if (ch == quote)
            {
                quote = '\0';
                current.Append(ch);
                continue;
            }

            if (ch == ',' && quote == '\0')
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        yield return current.ToString();
    }

    private static string UnquoteFontFamily(string value)
    {
        const char apostrophe = (char)39;
        const char quotationMark = (char)34;
        var trimmed = value.Trim();
        return trimmed.Length >= 2
               && ((trimmed[0] == quotationMark && trimmed[^1] == quotationMark)
                   || (trimmed[0] == apostrophe && trimmed[^1] == apostrophe))
            ? trimmed[1..^1].Trim()
            : trimmed;
    }

    public event EventHandler<string>? LanguageChanged;

    public void ReloadFromSettings()
    {
        _followSystemLanguage = _settingsService.Current.FollowSystemLanguage;
        _selectedLanguage = ResolveLanguage(_settingsService.Current.Language);
        OnPropertyChanged(string.Empty);
    }

    public void ApplySystemLanguageIfNeeded()
    {
        if (_settingsService.Current.FollowSystemLanguage)
        {
            ApplySystemLanguage();
        }
        else
        {
            ApplyLanguage(_settingsService.Current.Language);
        }
    }

    private void ApplySystemLanguage()
    {
        var cultureCode = GetSystemCultureCode() ?? CultureInfo.CurrentUICulture.Name;
        var resolved = ResolveLanguage(cultureCode);
        if (resolved.Code != _selectedLanguage.Code)
        {
            _selectedLanguage = resolved;
            _settingsService.Current.Language = resolved.Code;
            OnPropertyChanged(nameof(SelectedLanguage));
        }

        ApplyLanguage(_selectedLanguage.Code);
    }

    private static string? GetSystemCultureCode()
    {
        // Linux: prefer environment variables; these reflect the OS locale better than CurrentUICulture
        // after the app changes DefaultThreadCurrentUICulture.
        var raw =
            Environment.GetEnvironmentVariable("LC_ALL") ??
            Environment.GetEnvironmentVariable("LANGUAGE") ??
            Environment.GetEnvironmentVariable("LANG");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // LANGUAGE may contain multiple entries like: "en_US:en"; take the first.
        var first = raw.Split(':', StringSplitOptions.RemoveEmptyEntries)[0];

        // Strip encoding/modifiers, e.g. en_US.UTF-8 -> en_US
        first = first.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        first = first.Split('@', StringSplitOptions.RemoveEmptyEntries)[0];

        // Normalize underscore to dash.
        first = first.Replace('_', '-');

        return first;
    }

    private void ApplyLanguage(string cultureCode)
    {
        Localization.SetCulture(cultureCode);
        LanguageChanged?.Invoke(this, cultureCode);
    }

    private LocaleCultureInfo ResolveLanguage(string cultureCode)
    {
        var match = _availableLanguages.FirstOrDefault(l => l.Code == cultureCode);
        return match ?? _availableLanguages.First();
    }

    private static long GetDirectorySize(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    try
                    {
                        return new FileInfo(path).Length;
                    }
                    catch
                    {
                        return 0;
                    }
                })
                .Sum();
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            // i18n-ignore (data format)
            return $"{value} {units[unit]}";
        }

        // i18n-ignore (data format)
        return $"{display:0.##} {units[unit]}";
    }

    private static void OpenDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "explorer.exe"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "open"
                    : "xdg-open";
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(directory);
            using var _ = Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private void ScheduleSave()
    {
        if (_saveScheduled)
        {
            return;
        }

        _saveScheduled = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            _saveScheduled = false;
            await _settingsService.SaveAsync();
        });
    }
}

public sealed record SettingOption<T>(T Value, string Label);
