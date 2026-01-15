using ComCross.Shared.Interfaces;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Default serial port access manager for platforms that don't require special permission handling
/// (Windows, macOS, etc.)
/// </summary>
public sealed class DefaultSerialPortAccessManager : ISerialPortAccessManager
{
    public Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        // On Windows and macOS, serial port access doesn't require special permissions
        return Task.FromResult(true);
    }

    public Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        // Permission request not needed on these platforms
        return Task.FromResult(PermissionRequestResult.AlreadyGranted);
    }

    public string GetManualPermissionInstructions(string portPath)
    {
        // No special instructions needed for these platforms
        return string.Empty;
    }
}
