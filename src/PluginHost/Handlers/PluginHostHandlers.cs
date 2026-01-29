using System.Text.Json;
using ComCross.PluginHost.Runtime;
using ComCross.Shared.Models;

namespace ComCross.PluginHost.Handlers;

// Compatibility shim: legacy entrypoints forwarded to per-message handlers.
// New code should call the specific handler class directly.
internal static class PluginHostHandlers
{
    public static PluginHostResponse HandlePing(PluginHostRequest request, HostRuntime state)
        => PingHandler.Handle(request, state);

    public static PluginHostResponse HandleNotify(PluginHostRequest request, HostRuntime state)
        => NotifyHandler.Handle(request, state);

    public static PluginHostResponse HandleShutdown(PluginHostRequest request)
        => ShutdownHandler.Handle(request);

    public static PluginHostResponse HandleLanguageChanged(PluginHostRequest request)
        => LanguageChangedHandler.Handle(request);

    public static Task<PluginHostResponse> HandleConnect(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
        => ConnectHandler.HandleAsync(request, state, jsonOptions, cancellationToken);

    public static Task<PluginHostResponse> HandleDisconnect(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
        => DisconnectHandler.HandleAsync(request, state, jsonOptions, cancellationToken);

    public static PluginHostResponse HandleApplySharedMemorySegment(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions)
        => ApplySharedMemorySegmentHandler.Handle(request, state, jsonOptions);

    public static PluginHostResponse HandleSetBackpressure(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions)
        => SetBackpressureHandler.Handle(request, state, jsonOptions);

    public static Task<PluginHostResponse> HandleSendData(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
        => SendDataHandler.HandleAsync(request, state, jsonOptions, cancellationToken);

    public static PluginHostResponse HandleGetCapabilities(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions)
        => GetCapabilitiesHandler.Handle(request, state, jsonOptions);

    public static PluginHostResponse HandleGetUiState(
        PluginHostRequest request,
        HostRuntime state,
        JsonSerializerOptions jsonOptions)
        => GetUiStateHandler.Handle(request, state, jsonOptions);
}
