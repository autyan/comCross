namespace ComCross.Shared.Interfaces;

/// <summary>
/// Platform-specific serial port access manager
/// </summary>
public interface ISerialPortAccessManager
{
    /// <summary>
    /// Checks if the current user has permission to access a specific serial port device
    /// </summary>
    /// <param name="portPath">The device path (e.g., /dev/ttyUSB0, COM3)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to request temporary elevated permissions for a specific serial port device.
    /// This does NOT modify user groups permanently.
    /// </summary>
    /// <param name="portPath">The device path (e.g., /dev/ttyUSB0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the permission request</returns>
    Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user-friendly message explaining how to grant permissions manually
    /// </summary>
    /// <param name="portPath">The device path</param>
    string GetManualPermissionInstructions(string portPath);
}

/// <summary>
/// Result of a permission request operation
/// </summary>
public enum PermissionRequestResult
{
    /// <summary>
    /// Permission was already granted
    /// </summary>
    AlreadyGranted,

    /// <summary>
    /// Permission was successfully granted (may require logout/login)
    /// </summary>
    Granted,

    /// <summary>
    /// User denied the permission request
    /// </summary>
    Denied,

    /// <summary>
    /// Automatic permission request is not supported on this platform
    /// </summary>
    NotSupported,

    /// <summary>
    /// Permission request failed due to an error
    /// </summary>
    Failed
}
