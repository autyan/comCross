using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Adapters.Serial;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EventBus _eventBus;
    private readonly MessageStreamService _messageStream;
    private readonly DeviceService _deviceService;
    private readonly ConfigService _configService;
    private readonly ILocalizationService _localization;
    private Session? _activeSession;
    private string _searchQuery = string.Empty;
    private bool _isConnected;

    public ObservableCollection<Session> Sessions { get; } = new();
    public ObservableCollection<Device> Devices { get; } = new();
    public ObservableCollection<LogMessage> Messages { get; } = new();

    public Session? ActiveSession
    {
        get => _activeSession;
        set
        {
            if (_activeSession != value)
            {
                _activeSession = value;
                OnPropertyChanged();
                LoadMessages();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterMessages();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Title => _localization.GetString("app.title");

    public ILocalizationService Localization => _localization;
    
    public LocalizedStringsViewModel LocalizedStrings { get; }

    public MainWindowViewModel()
    {
        _eventBus = new EventBus();
        _messageStream = new MessageStreamService();
        var adapter = new SerialAdapter();
        _deviceService = new DeviceService(adapter, _eventBus, _messageStream);
        _configService = new ConfigService();
        _localization = new LocalizationService();
        LocalizedStrings = new LocalizedStringsViewModel(_localization);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // Load devices
        var devices = await _deviceService.ListDevicesAsync();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        // Load workspace state
        var state = await _configService.LoadWorkspaceStateAsync();
        if (state != null)
        {
            // Restore sessions (but don't auto-connect)
            foreach (var sessionState in state.Sessions)
            {
                var session = new Session
                {
                    Id = sessionState.Id,
                    Name = sessionState.Name,
                    Port = sessionState.Port,
                    BaudRate = sessionState.Settings.BaudRate,
                    Status = SessionStatus.Disconnected,
                    Settings = sessionState.Settings
                };
                Sessions.Add(session);
            }

            // Restore UI state
            if (state.UiState?.ActiveSessionId != null)
            {
                ActiveSession = Sessions.FirstOrDefault(s => s.Id == state.UiState.ActiveSessionId);
            }
        }
    }

    public async Task ConnectAsync(string port, int baudRate, string name)
    {
        try
        {
            var sessionId = $"session-{Guid.NewGuid()}";
            var settings = new SerialSettings { BaudRate = baudRate };
            
            var session = await _deviceService.ConnectAsync(sessionId, port, name, settings);
            Sessions.Add(session);
            ActiveSession = session;
            IsConnected = true;

            // Subscribe to messages
            _messageStream.Subscribe(sessionId, message =>
            {
                Messages.Add(message);
            });
        }
        catch (Exception ex)
        {
            // Handle error
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (ActiveSession != null)
        {
            await _deviceService.DisconnectAsync(ActiveSession.Id);
            ActiveSession.Status = SessionStatus.Disconnected;
            IsConnected = false;
        }
    }

    public async Task SendAsync(string message, bool hex, bool addCr, bool addLf)
    {
        if (ActiveSession == null || !IsConnected) return;

        try
        {
            var data = hex 
                ? Convert.FromHexString(message.Replace(" ", ""))
                : System.Text.Encoding.UTF8.GetBytes(message);

            if (addCr || addLf)
            {
                var suffix = (addCr ? "\r" : "") + (addLf ? "\n" : "");
                var suffixBytes = System.Text.Encoding.UTF8.GetBytes(suffix);
                data = data.Concat(suffixBytes).ToArray();
            }

            await _deviceService.SendAsync(ActiveSession.Id, data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Send failed: {ex.Message}");
        }
    }

    public void ClearMessages()
    {
        if (ActiveSession != null)
        {
            _messageStream.Clear(ActiveSession.Id);
            Messages.Clear();
        }
    }

    private void LoadMessages()
    {
        Messages.Clear();
        if (ActiveSession != null)
        {
            var messages = _messageStream.GetMessages(ActiveSession.Id, 0, 1000);
            foreach (var message in messages)
            {
                Messages.Add(message);
            }
        }
    }

    private void FilterMessages()
    {
        Messages.Clear();
        if (ActiveSession != null && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            var filtered = _messageStream.Search(ActiveSession.Id, SearchQuery);
            foreach (var message in filtered)
            {
                Messages.Add(message);
            }
        }
        else
        {
            LoadMessages();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
