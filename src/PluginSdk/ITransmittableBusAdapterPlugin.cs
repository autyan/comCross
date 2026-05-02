namespace ComCross.PluginSdk;

public interface ITransmittableBusAdapterPlugin
{
    Task<PluginCommandResult> SendAsync(PluginSendCommand command, CancellationToken cancellationToken);
}
