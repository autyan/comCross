using ComCross.Shared.Models;

namespace ComCross.PluginHost.Routing;

internal sealed class RoleGatekeeper
{
    private readonly string _role;

    public RoleGatekeeper(string role)
    {
        _role = role;
    }

    public bool IsAllowed(string messageType)
    {
        if (string.Equals(_role, "ui", StringComparison.Ordinal))
        {
            return messageType is PluginHostMessageTypes.Ping
                or PluginHostMessageTypes.Notify
                or PluginHostMessageTypes.GetCapabilities
                or PluginHostMessageTypes.GetUiState
                or PluginHostMessageTypes.GetTransmitTargets
                or PluginHostMessageTypes.InitializeSessionState
                or PluginHostMessageTypes.LanguageChanged
                or PluginHostMessageTypes.Shutdown;
        }

        return messageType is PluginHostMessageTypes.Ping
            or PluginHostMessageTypes.Notify
            or PluginHostMessageTypes.GetCapabilities
            or PluginHostMessageTypes.GetUiState
            or PluginHostMessageTypes.GetTransmitTargets
            or PluginHostMessageTypes.InitializeSessionState
            or PluginHostMessageTypes.ApplySharedMemorySegment
            or PluginHostMessageTypes.Connect
            or PluginHostMessageTypes.Disconnect
            or PluginHostMessageTypes.SetBackpressure
            or PluginHostMessageTypes.ExecuteAction
            or PluginHostMessageTypes.SendData
            or PluginHostMessageTypes.Shutdown;
    }
}
