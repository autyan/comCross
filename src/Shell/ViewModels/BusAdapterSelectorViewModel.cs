using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ComCross.Shared.Models;
using ComCross.Shell.Models;
using ComCross.Shared.Services;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;
using ComCross.Shell.Views;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    private const string PluginAdapterPrefix = "plugin:";
    private BusAdapterInfo? _selectedAdapter;
    private Control? _configPanel; // Changed from UserControl to Control
    private string? _activeSessionId;
    private readonly BusAdapterInfo _serialAdapter;
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _stateManager;

    public BusAdapterSelectorViewModel(ILocalizationService localization, PluginUiRenderer uiRenderer, PluginUiStateManager stateManager)
        : base(localization)
    {
        _uiRenderer = uiRenderer;
        _stateManager = stateManager;
        // Initialize available adapters
        _serialAdapter = new BusAdapterInfo
        {
            Id = "serial",
            Name = "Serial (RS232)",
            Icon = "🔌",
            Description = "Serial port communication (RS232/RS485/RS422)",
            IsEnabled = true,
            PluginId = "system.serial",
            CapabilityId = "serial",
            UiSchema = "{\"fields\":[" +
                "{\"key\":\"port\",\"labelKey\":\"sidebar.selectPort\",\"type\":\"select\",\"optionsStatePath\":\"system.serial.ports\"}," +
                "{\"key\":\"baudRate\",\"labelKey\":\"sidebar.baudRate\",\"type\":\"select\",\"options\":[\"9600\",\"19200\",\"38400\",\"57600\",\"115200\",\"230400\",\"460800\"]}," +
                "{\"key\":\"dataBits\",\"labelKey\":\"sidebar.dataBits\",\"type\":\"select\",\"options\":[\"7\",\"8\"]}," +
                "{\"key\":\"parity\",\"labelKey\":\"sidebar.parity\",\"type\":\"select\",\"options\":[\"None\",\"Odd\",\"Even\"]}," +
                "{\"key\":\"stopBits\",\"labelKey\":\"sidebar.stopBits\",\"type\":\"select\",\"options\":[\"1\",\"1.5\",\"2\"]}" +
                "],\"actions\":[" +
                "{\"id\":\"refresh\",\"labelKey\":\"sidebar.refreshPorts\",\"icon\":\"refresh\"}," +
                "{\"id\":\"connect\",\"labelKey\":\"menu.connect\"}" +
                "]}"
        };

        AvailableAdapters = new ObservableCollection<BusAdapterInfo>
        {
            _serialAdapter,
            // Future adapters (disabled for now)
            new BusAdapterInfo
            {
                Id = "tcp-client",
                Name = "TCP/IP Client",
                Icon = "🌐",
                Description = "TCP/IP client connection",
                IsEnabled = false,
                ConfigPanelType = null // TODO: Create TcpClientConfigPanel
            },
            new BusAdapterInfo
            {
                Id = "tcp-server",
                Name = "TCP/IP Server",
                Icon = "🌐",
                Description = "TCP/IP server (listen for connections)",
                IsEnabled = false,
                ConfigPanelType = null // TODO: Create TcpServerConfigPanel
            },
            new BusAdapterInfo
            {
                Id = "udp",
                Name = "UDP",
                Icon = "📡",
                Description = "UDP datagram communication",
                IsEnabled = false,
                ConfigPanelType = null // TODO: Create UdpConfigPanel
            }
        };

        // Select Serial adapter by default
        SelectedAdapter = AvailableAdapters[0];
    }

    /// <summary>
    /// Available bus adapters
    /// </summary>
    public ObservableCollection<BusAdapterInfo> AvailableAdapters { get; }

    public void UpdatePluginAdapters(IReadOnlyList<PluginCapabilityLaunchOption> options)
    {
        var previouslySelectedId = SelectedAdapter?.Id;

        // Remove existing plugin-backed adapters.
        for (var index = AvailableAdapters.Count - 1; index >= 0; index--)
        {
            var existing = AvailableAdapters[index];
            if (existing.Id.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal))
            {
                AvailableAdapters.RemoveAt(index);
            }
        }

        if (options.Count > 0)
        {
            foreach (var option in options)
            {
                AvailableAdapters.Add(new BusAdapterInfo
                {
                    Id = $"{PluginAdapterPrefix}{option.PluginId}:{option.CapabilityId}",
                    Name = $"{option.PluginName} / {option.CapabilityName}",
                    Icon = "🧩",
                    Description = option.CapabilityDescription,
                    IsEnabled = true,
                    ConfigPanelType = null,
                    PluginId = option.PluginId,
                    CapabilityId = option.CapabilityId,
                    DefaultParametersJson = option.DefaultParametersJson,
                    JsonSchema = option.JsonSchema,
                    UiSchema = option.UiSchema
                });
            }
        }

        // Restore selection if possible; otherwise fall back to Serial.
        if (!string.IsNullOrWhiteSpace(previouslySelectedId))
        {
            var match = AvailableAdapters.FirstOrDefault(a => string.Equals(a.Id, previouslySelectedId, StringComparison.Ordinal));
            if (match is not null)
            {
                SelectedAdapter = match;
                return;
            }
        }

        SelectedAdapter = _serialAdapter;
    }

    public void SelectAdapterById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        var adapter = AvailableAdapters.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (adapter != null)
        {
            SelectedAdapter = adapter;
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
                LoadConfigPanel();
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
        _activeSessionId = session?.Id;
        
        if (session != null && !string.IsNullOrEmpty(session.AdapterId))
        {
            // Restore adapter selection
            SelectAdapterById(session.AdapterId);

            // If it's a plugin adapter and we have parameters, sync them to StateManager
            if (session.AdapterId.StartsWith(PluginAdapterPrefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(session.ParametersJson))
            {
                _stateManager.UpdateSessionState(session.Id, session.ParametersJson);
            }
        }
        else
        {
            // Reset to Serial if no active session
            SelectAdapterById("serial");
        }
    }

    /// <summary>
    /// Load the configuration panel for the selected adapter
    /// </summary>
    private void LoadConfigPanel()
    {
        if (_selectedAdapter?.ConfigPanelType != null)
        {
            try
            {
                // Create instance of the config panel
                var panel = Activator.CreateInstance(_selectedAdapter.ConfigPanelType) as Control;
                ConfigPanel = panel;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load config panel for {_selectedAdapter.Name}: {ex.Message}");
                ConfigPanel = null;
            }
        }
        else if (_selectedAdapter?.PluginId != null)
        {
             // Plugin-backed adapter: Use PluginUiRenderer
             var uiSchema = PluginUiSchema.TryParse(_selectedAdapter.UiSchema);
             var fields = uiSchema?.Fields;
             
             // If no fields, we don't show a panel (or could fallback to JsonSchema, but keeping it simple for now)
             if (fields != null && fields.Count > 0 && _selectedAdapter.CapabilityId != null)
             {
                 var schema = new PluginUiSchema { Fields = fields };
                 var container = _uiRenderer.GetOrRender(_selectedAdapter.PluginId, _selectedAdapter.CapabilityId, schema, _activeSessionId, "sidebar-config");
                 if (container is AvaloniaPluginUiContainer avaloniaContainer)
                 {
                     ConfigPanel = avaloniaContainer.GetPanel();
                 }
                 else
                 {
                     ConfigPanel = null;
                 }
             }
             else
             {
                 ConfigPanel = null;
             }
        }
        else
        {
            // No custom config panel, use default or null
            ConfigPanel = null;
        }
    }
}
