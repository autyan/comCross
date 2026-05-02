namespace ComCross.PluginSdk;

/// <summary>
/// Optional extension for device plugins that support multiple concurrent sessions.
///
/// The host will inject a dedicated shared-memory writer per sessionId.
/// Plugins can keep per-session state and write to the corresponding session segment.
/// </summary>
public interface IMultiSessionDevicePlugin : IDevicePlugin
{
    /// <summary>
    /// Set (or replace) the shared memory writer for a specific session.
    /// </summary>
    void SetSharedMemoryWriter(string sessionId, ISharedMemoryWriter writer);

    /// <summary>
    /// Clear the shared memory writer for a specific session.
    /// Called when the session ends or the host is resetting.
    /// </summary>
    void ClearSharedMemoryWriter(string sessionId);
}
