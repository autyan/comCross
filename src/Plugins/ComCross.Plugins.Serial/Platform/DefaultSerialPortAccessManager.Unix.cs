namespace ComCross.Plugins.Serial.Platform;

/// <summary>
/// Default serial port access manager for platforms that don't require special permission handling.
/// </summary>
public sealed class DefaultSerialPortAccessManager : ISerialPortAccessManager
{
    public Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
        => Task.FromResult(PermissionRequestResult.AlreadyGranted);

    public string GetManualPermissionInstructions(string portPath) => string.Empty;
}
