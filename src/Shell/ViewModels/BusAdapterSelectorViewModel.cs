using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using ComCross.Shell.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for bus adapter selection and configuration
/// </summary>
public sealed class BusAdapterSelectorViewModel : BaseViewModel
{
    private BusAdapterInfo? _selectedAdapter;
    private UserControl? _configPanel;

    public BusAdapterSelectorViewModel(ILocalizationService localization)
        : base(localization)
    {
        // Initialize available adapters
        AvailableAdapters = new ObservableCollection<BusAdapterInfo>
        {
            new BusAdapterInfo
            {
                Id = "serial",
                Name = "Serial (RS232)",
                Icon = "🔌",
                Description = "Serial port communication (RS232/RS485/RS422)",
                IsEnabled = true,
                ConfigPanelType = null // Will use device selector in LeftSidebar
            },
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
