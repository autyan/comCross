namespace ComCross.PluginSdk;

/// <summary>
/// Optional extension for BusAdapter plugins that support a standardized connect/disconnect lifecycle.
///
/// Parameters are plugin-defined (JSON) and should be validated by the host using the declared JsonSchema
/// in the corresponding capability descriptor.
/// </summary>
public interface IConnectableBusAdapterPlugin : IBusAdapterPlugin
{
    Task<PluginConnectResult> ConnectAsync(PluginConnectCommand command, CancellationToken cancellationToken);

    Task<PluginCommandResult> DisconnectAsync(PluginDisconnectCommand command, CancellationToken cancellationToken);
}
