using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Service for handling serial port permission requests
/// </summary>
public sealed class SerialPortPermissionService
{
    private readonly ISerialPortAccessManager _accessManager;
    private readonly NotificationService _notificationService;

    public SerialPortPermissionService(
        ISerialPortAccessManager accessManager,
        NotificationService notificationService)
    {
        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    /// <summary>
    /// Attempts to request permission for a serial port and notifies the user of the result
    /// </summary>
    public async Task<bool> RequestPermissionAsync(
        string portPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _accessManager.RequestAccessPermissionAsync(portPath, cancellationToken);

        switch (result)
        {
            case PermissionRequestResult.AlreadyGranted:
                return true;

            case PermissionRequestResult.Granted:
                await _notificationService.AddAsync(
                    NotificationCategory.Connection,
                    NotificationLevel.Info,
                    "notification.permission.temporaryGranted",
                    new object[] { portPath },
                    cancellationToken);
                return true;

            case PermissionRequestResult.Denied:
            case PermissionRequestResult.Failed:
            case PermissionRequestResult.NotSupported:
                await _notificationService.AddAsync(
                    NotificationCategory.Connection,
                    NotificationLevel.Error,
                    "notification.permission.failed",
                    new object[] { portPath },
                    cancellationToken);
                
                // Log manual instructions to app log
                var instructions = _accessManager.GetManualPermissionInstructions(portPath);
                if (!string.IsNullOrEmpty(instructions))
                {
                    Console.WriteLine($"Manual permission instructions:\n{instructions}");
                }
                
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets manual permission instructions for a port
    /// </summary>
    public string GetManualInstructions(string portPath)
    {
        return _accessManager.GetManualPermissionInstructions(portPath);
    }
}
