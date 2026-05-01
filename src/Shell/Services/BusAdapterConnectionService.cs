using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Shared.Models;

namespace ComCross.Shell.Services;

public sealed class BusAdapterConnectionService
{
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly IWorkspaceCoordinator _workspaceCoordinator;

    public BusAdapterConnectionService(
        ICapabilityDispatcher dispatcher,
        IWorkspaceCoordinator workspaceCoordinator)
    {
        _dispatcher = dispatcher;
        _workspaceCoordinator = workspaceCoordinator;
    }

    public Task<IEnumerable<Session>> GetActiveSessionsAsync()
        => _workspaceCoordinator.GetActiveSessionsAsync();

    public async Task<Session?> GetActiveSessionAsync(string sessionId)
    {
        var sessions = await _workspaceCoordinator.GetActiveSessionsAsync();
        return sessions.FirstOrDefault(session => string.Equals(session.Id, sessionId, StringComparison.Ordinal));
    }

    public async Task<Session?> FindSerialPortConflictAsync(string pluginId, string capabilityId, string desiredPort)
    {
        if (string.IsNullOrWhiteSpace(desiredPort))
        {
            return null;
        }

        var sessions = await _workspaceCoordinator.GetActiveSessionsAsync();
        return sessions.FirstOrDefault(session =>
            session.Status == SessionStatus.Connected
            && string.Equals(session.PluginId, pluginId, StringComparison.Ordinal)
            && string.Equals(session.CapabilityId, capabilityId, StringComparison.Ordinal)
            && string.Equals(TryGetCommittedParameterString(session.ParametersJson, "port"), desiredPort, StringComparison.Ordinal));
    }

    public Task ConnectPluginAsync(
        string pluginId,
        string capabilityId,
        string? targetSessionId,
        IDictionary<string, object> parameters)
    {
        var payload = new
        {
            CapabilityId = capabilityId,
            SessionId = targetSessionId,
            Parameters = parameters
        };

        return _dispatcher.DispatchAsync(pluginId, targetSessionId, PluginHostMessageTypes.Connect, payload);
    }

    public Task DisconnectPluginAsync(string? pluginId, string sessionId)
        => _dispatcher.DispatchAsync(pluginId, sessionId, PluginHostMessageTypes.Disconnect, null);

    public Task<Session> ConnectScopedResourceAsync(
        string pluginId,
        string capabilityId,
        string parametersJson,
        string? sessionName,
        string scopeSessionId,
        string resourceKind,
        string resourceId)
        => _workspaceCoordinator.ConnectAsync(
            pluginId,
            capabilityId,
            parametersJson,
            sessionName,
            scopeSessionId,
            resourceKind,
            resourceId);

    public Task CloseSessionAsync(string sessionId)
        => _workspaceCoordinator.CloseSessionAsync(sessionId);

    public Task DeleteSessionAsync(string sessionId)
        => _workspaceCoordinator.DeleteSessionAsync(sessionId);

    private static string? TryGetCommittedParameterString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty(key, out var prop))
            {
                return null;
            }

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }
        catch
        {
            return null;
        }
    }
}
