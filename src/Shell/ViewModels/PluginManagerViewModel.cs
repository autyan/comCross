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

namespace ComCross.Shell.ViewModels;

public sealed class PluginManagerViewModel : BaseViewModel
{
    private readonly PluginDiscoveryService _discoveryService;
    private readonly PluginRuntimeService _runtimeService;
    private readonly SettingsService _settingsService;
    private string _pluginsDirectory;
    private List<PluginRuntime> _runtimes = new();

    public PluginManagerViewModel(
        ILocalizationService localization,
        PluginDiscoveryService discoveryService,
        PluginRuntimeService runtimeService,
        SettingsService settingsService)
        : base(localization)
    {
        _discoveryService = discoveryService;
        _runtimeService = runtimeService;
        _settingsService = settingsService;
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
        _runtimes = _runtimeService.LoadPlugins(items, _settingsService.Current.Plugins.Enabled).ToList();

        foreach (var runtime in _runtimes)
        {
            Plugins.Add(new PluginItemViewModel(runtime, _settingsService.Current.Plugins.Enabled, L));
        }

        await Task.CompletedTask;
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
        }
    }

    public void NotifyLanguageChanged(string cultureCode, Action<PluginRuntime, Exception, bool>? onError = null)
    {
        NotifyPlugins(PluginNotification.LanguageChanged(cultureCode), onError);
    }

    public void NotifyPlugins(PluginNotification notification, Action<PluginRuntime, Exception, bool>? onError = null)
    {
        _runtimeService.Notify(_runtimes, notification, onError);
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

public sealed class PluginItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _statusText = string.Empty;
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

    public void UpdateState(PluginRuntime runtime, ILocalizationStrings localizedStrings)
    {
        State = runtime.State;
        RefreshStatus(localizedStrings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
