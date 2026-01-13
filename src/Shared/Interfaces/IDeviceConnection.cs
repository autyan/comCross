using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Device connection abstraction
/// </summary>
public interface IDeviceConnection : IDisposable
{
    string Port { get; }
    bool IsOpen { get; }
    SessionStatus Status { get; }

    Task OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default);
    Task CloseAsync();
    Task<int> WriteAsync(byte[] data, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(int maxBytes = 1024, CancellationToken cancellationToken = default);
    
    event EventHandler<byte[]>? DataReceived;
    event EventHandler<string>? ErrorOccurred;
}
