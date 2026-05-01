using System.Text.Json;
using ComCross.Shared.Models;
using ComCross.PluginHost.Runtime;
using ComCross.PluginHost.Routing;
using ComCross.PluginHost.Handlers;

namespace ComCross.PluginHost.Ipc;

internal sealed class PluginHostRequestRouter
{
    private readonly RoleGatekeeper _gatekeeper;
    private readonly HostRuntime _state;
    private readonly JsonSerializerOptions _jsonOptions;

    public PluginHostRequestRouter(string role, HostRuntime state, JsonSerializerOptions jsonOptions)
    {
        _gatekeeper = new RoleGatekeeper(role);
        _state = state;
        _jsonOptions = jsonOptions;
    }

    public async Task<PluginHostResponse> HandleAsync(PluginHostRequest request, CancellationToken cancellationToken)
    {
        if (!_gatekeeper.IsAllowed(request.Type))
        {
            return new PluginHostResponse(request.Id, false, $"Message '{request.Type}' is not supported by current role.");
        }

        switch (request.Type)
        {
            case PluginHostMessageTypes.Ping:
                return PingHandler.Handle(request, _state);
            case PluginHostMessageTypes.Notify:
                return NotifyHandler.Handle(request, _state);
            case PluginHostMessageTypes.GetCapabilities:
                return GetCapabilitiesHandler.Handle(request, _state, _jsonOptions);
            case PluginHostMessageTypes.Connect:
                return await ConnectHandler.HandleAsync(request, _state, _jsonOptions, cancellationToken);
            case PluginHostMessageTypes.Disconnect:
                return await DisconnectHandler.HandleAsync(request, _state, _jsonOptions, cancellationToken);
            case PluginHostMessageTypes.GetUiState:
                return GetUiStateHandler.Handle(request, _state, _jsonOptions);
            case PluginHostMessageTypes.InitializeSessionState:
                return await InitializeSessionStateHandler.HandleAsync(request, _state, _jsonOptions, cancellationToken);
            case PluginHostMessageTypes.ApplySharedMemorySegment:
                return ApplySharedMemorySegmentHandler.Handle(request, _state, _jsonOptions);
            case PluginHostMessageTypes.SetBackpressure:
                return SetBackpressureHandler.Handle(request, _state, _jsonOptions);
            case PluginHostMessageTypes.ExecuteAction:
                return await ExecuteActionHandler.HandleAsync(request, _state, _jsonOptions, cancellationToken);
            case PluginHostMessageTypes.SendData:
                return await SendDataHandler.HandleAsync(request, _state, _jsonOptions, cancellationToken);
            case PluginHostMessageTypes.LanguageChanged:
                return LanguageChangedHandler.Handle(request);
            case PluginHostMessageTypes.Shutdown:
                return ShutdownHandler.Handle(request);
            default:
                return new PluginHostResponse(request.Id, false, $"Unknown request type: {request.Type}");
        }
    }
}
