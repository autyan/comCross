using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Platform-specific device adapter
/// </summary>
public interface IDeviceAdapter
{
    /// <summary>
    /// Lists all available serial ports
    /// </summary>
    Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection to the specified device
    /// </summary>
    IDeviceConnection OpenConnection(string port);
}
