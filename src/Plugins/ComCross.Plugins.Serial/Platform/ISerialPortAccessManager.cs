namespace ComCross.Plugins.Serial.Platform;

/// <summary>
/// Platform-specific serial port access manager.
/// </summary>
public interface ISerialPortAccessManager
{
    Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default);

    Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default);

    string GetManualPermissionInstructions(string portPath);
}

public enum PermissionRequestResult
{
    AlreadyGranted,
    Granted,
    Denied,
    NotSupported,
    Failed
}
