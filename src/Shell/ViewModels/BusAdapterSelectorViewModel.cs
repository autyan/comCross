using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ComCross.Shell.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    private const string PluginAdapterPrefix = "plugin:";
    private BusAdapterInfo? _selectedAdapter;
    private UserControl? _configPanel;
    private readonly BusAdapterInfo _serialAdapter;

    public BusAdapterSelectorViewModel(ILocalizationService localization)
        : base(localization)
    {
        // Initialize available adapters
        _serialAdapter = new BusAdapterInfo
        {
            Id = "serial",
            Name = "Serial (RS232)",
            Icon = "🔌",
            Description = "Serial port communication (RS232/RS485/RS422)",
            IsEnabled = true,
            ConfigPanelType = null // Will use device selector in LeftSidebar
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
                    DefaultParametersJson = option.DefaultParametersJson
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
    public UserControl? ConfigPanel
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
                var panel = Activator.CreateInstance(_selectedAdapter.ConfigPanelType) as UserControl;
                ConfigPanel = panel;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load config panel for {_selectedAdapter.Name}: {ex.Message}");
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
