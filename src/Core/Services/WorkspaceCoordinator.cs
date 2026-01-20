using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Core.Models;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Domain facade that coordinates workspace, workload, and session lifecycle operations.
/// Reduces the cognitive load on ViewModels.
/// </summary>
public interface IWorkspaceCoordinator
{
    Task<IEnumerable<Session>> GetActiveSessionsAsync();
    Task<Workload?> GetCurrentWorkloadAsync();
    
    Task SwitchWorkloadAsync(string workloadId);
    Task CloseSessionAsync(string sessionId);
    
    // Workspace Management
    Task<WorkspaceState> LoadStateAsync();
    Task SaveCurrentStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll);
    Task EnsureDefaultWorkloadAsync();
    
    // Session Operations
    Task<Session> ConnectAsync(string pluginId, string capabilityId, string parametersJson, string? sessionName = null);
    Task DeleteSessionAsync(string sessionId);
    Task SendMessageAsync(string sessionId, string message, MessageFormat format, bool addCr, bool addLf);
    Task SendDataAsync(string sessionId, byte[] data);
    void ClearMessages(string sessionId);
    void SubscribeToMessages(string sessionId, Action<LogMessage> callback);
    
    // Export Operations
    Task<string> ExportAsync(Session session, string? searchQuery = null, string? customFilePath = null);

    // Statistics aggregated by coordinator
    long TotalRxBytes { get; }
    long TotalTxBytes { get; }
    event EventHandler? StatisticsUpdated;
}

public class WorkspaceCoordinator : IWorkspaceCoordinator
{
    private readonly WorkspaceService _workspaceService;
    private readonly WorkloadService _workloadService;
    private readonly ExportService _exportService;
    private readonly DeviceService _deviceService;
    private readonly SettingsService _settingsService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkspaceCoordinator> _logger;
    
    private long _totalRxBytes;
    private long _totalTxBytes;

    public long TotalRxBytes => _totalRxBytes;
    public long TotalTxBytes => _totalTxBytes;
    public event EventHandler? StatisticsUpdated;

    public WorkspaceCoordinator(
        WorkspaceService workspaceService,
        WorkloadService workloadService,
        ExportService exportService,
        DeviceService deviceService,
        SettingsService settingsService,
        IEventBus eventBus,
        ILogger<WorkspaceCoordinator> logger)
    {
        _workspaceService = workspaceService;
        _workloadService = workloadService;
        _exportService = exportService;
        _deviceService = deviceService;
        _settingsService = settingsService;
        _eventBus = eventBus;
        _logger = logger;

        // Statistics calculation (can be moved to a BackgroundService if needed, but for now just aggregate)
        _eventBus.Subscribe<ActiveWorkloadChangedEvent>(_ => UpdateStatistics());
        _eventBus.Subscribe<SessionCreatedEvent>(_ => UpdateStatistics());
        _eventBus.Subscribe<SessionClosedEvent>(_ => UpdateStatistics());
    }

    private void UpdateStatistics()
    {
        // Simple aggregation for now. In a real-world scenario, this might need 
        // to be more performant or push-based from sessions.
        Task.Run(async () => {
            var sessions = await _workspaceService.GetActiveSessionsAsync();
            var rx = sessions.Sum(s => s.RxBytes);
            var tx = sessions.Sum(s => s.TxBytes);
            
            if (rx != _totalRxBytes || tx != _totalTxBytes)
            {
                _totalRxBytes = rx;
                _totalTxBytes = tx;
                StatisticsUpdated?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public async Task<IEnumerable<Session>> GetActiveSessionsAsync()
    {
        return await _workspaceService.GetActiveSessionsAsync();
    }

    public async Task<Workload?> GetCurrentWorkloadAsync()
    {
        var activeId = await _workloadService.GetActiveWorkloadIdAsync();
        if (string.IsNullOrWhiteSpace(activeId))
        {
            return null;
        }

        return await _workloadService.GetWorkloadAsync(activeId);
    }

    public async Task SwitchWorkloadAsync(string workloadId)
    {
        _logger.LogInformation("Switching to workload {WorkloadId}", workloadId);
        var ok = await Task.Run(() => _workloadService.SetActiveWorkload(workloadId));
        if (ok)
        {
            _eventBus.Publish(new ActiveWorkloadChangedEvent(workloadId));
        }
        else
        {
            _logger.LogWarning("Workload not found: {WorkloadId}", workloadId);
        }
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        _logger.LogInformation("Closing session {SessionId}", sessionId);
        await _workspaceService.DisconnectAsync(sessionId);
    }

    public Task<WorkspaceState> LoadStateAsync() => _workspaceService.LoadStateAsync();

    public Task SaveCurrentStateAsync(IEnumerable<Session> sessions, Session? activeSession, bool autoScroll)
        => _workspaceService.SaveCurrentStateAsync(sessions, activeSession, autoScroll);

    public Task EnsureDefaultWorkloadAsync() => _workloadService.EnsureDefaultWorkloadAsync();

    public async Task<Session> ConnectAsync(string pluginId, string capabilityId, string parametersJson, string? sessionName = null)
    {
        return await _workspaceService.ConnectAsync(pluginId, capabilityId, parametersJson, sessionName);
    }

    public Task DeleteSessionAsync(string sessionId) => _workspaceService.DeleteSessionAsync(sessionId);

    public Task SendMessageAsync(string sessionId, string message, MessageFormat format, bool addCr, bool addLf)
        => _workspaceService.SendMessageAsync(sessionId, message, format, addCr, addLf);

    public Task SendDataAsync(string sessionId, byte[] data) => _workspaceService.SendDataAsync(sessionId, data);

    public void ClearMessages(string sessionId) => _workspaceService.ClearMessages(sessionId);

    public void SubscribeToMessages(string sessionId, Action<LogMessage> callback)
        => _workspaceService.SubscribeToMessages(sessionId, callback);

    public Task<string> ExportAsync(Session session, string? searchQuery = null, string? customFilePath = null)
        => _exportService.ExportAsync(session, searchQuery, customFilePath);
}
