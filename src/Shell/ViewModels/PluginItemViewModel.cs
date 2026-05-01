using System;
using ComCross.Core.Services;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

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
    string? Icon,
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

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CapabilitiesText));
        OnPropertyChanged(nameof(CanConnect));
    }
}
