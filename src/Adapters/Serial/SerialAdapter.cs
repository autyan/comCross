using System.IO.Ports;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Serial port adapter implementation
/// </summary>
public sealed class SerialAdapter : IDeviceAdapter
{
    public Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var ports = SerialPort.GetPortNames();
        var devices = ports.Select(port => new Device
        {
            Port = port,
            Name = port,
            Description = $"Serial Port {port}",
            Manufacturer = "Unknown",
            IsFavorite = false
        }).ToList();

        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }

    public IDeviceConnection OpenConnection(string port)
    {
        return new SerialConnection(port);
    }
}
