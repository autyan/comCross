namespace ComCross.PluginSdk;

/// <summary>
/// BusAdapter plugin contract.
///
/// - Provides schema-driven capabilities to the host.
/// - Can declare shared memory requirements per capability.
/// - Receives shared memory writer injection from the host.
/// - Handles backpressure.
///
/// Note: command execution (Connect/Disconnect/Notify) is defined at IPC layer and may evolve.
/// </summary>
public interface IBusAdapterPlugin : IDevicePlugin, IPluginCapabilityProvider
{
}
