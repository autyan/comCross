using System.ComponentModel;
using System.Diagnostics;
using ComCross.PluginSdk.Resources;

namespace ComCross.PluginSdk.Platform;

/// <summary>
/// Linux helpers for serial device permissions.
///
/// Typical issue: opening <c>/dev/ttyUSB0</c> requires membership in <c>dialout</c> or a udev rule.
/// For a low-risk temporary fix, the SDK can attempt <c>pkexec chmod 666 /dev/ttyXXX</c>.
/// </summary>
public static class LinuxSerialDevicePermissions
{
    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <summary>
    /// Checks whether the current process can open the device path for read/write.
    /// </summary>
    public static Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupported)
        {
            // Non-Linux platforms typically do not need this check.
            return Task.FromResult(true);
        }

        if (string.IsNullOrWhiteSpace(portPath))
        {
            return Task.FromResult(false);
        }

        var devicePath = ConvertToDevicePath(portPath);
        if (!File.Exists(devicePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            using var _ = File.Open(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return Task.FromResult(true);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
        catch (IOException)
        {
            // Device busy is still a strong signal that permission is OK.
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Attempts to request temporary access by invoking an elevation helper (pkexec/gksu) to chmod the device.
    /// This does NOT modify user groups.
    /// </summary>
    public static async Task<PrivilegeRequestResult> RequestTemporaryAccessAsync(string portPath, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return PrivilegeRequestResult.NotSupported;
        }

        if (string.IsNullOrWhiteSpace(portPath))
        {
            return PrivilegeRequestResult.Failed;
        }

        var devicePath = ConvertToDevicePath(portPath);
        if (!File.Exists(devicePath))
        {
            return PrivilegeRequestResult.Failed;
        }

        if (await HasAccessPermissionAsync(devicePath, cancellationToken).ConfigureAwait(false))
        {
            return PrivilegeRequestResult.AlreadyGranted;
        }

        // Preferred: pkexec chmod 666 <device>
        var pkexec = await RunCommandAsync(
            fileName: "pkexec",
            args: new[] { "chmod", "666", devicePath },
            cancellationToken).ConfigureAwait(false);

        if (pkexec == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken).ConfigureAwait(false))
        {
            return PrivilegeRequestResult.Granted;
        }

        // Legacy fallbacks (may not exist on modern distros)
        var gksu = await RunCommandAsync(
            fileName: "gksu",
            args: new[] { "chmod", "666", devicePath },
            cancellationToken).ConfigureAwait(false);

        if (gksu == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken).ConfigureAwait(false))
        {
            return PrivilegeRequestResult.Granted;
        }

        var gksudo = await RunCommandAsync(
            fileName: "gksudo",
            args: new[] { "chmod", "666", devicePath },
            cancellationToken).ConfigureAwait(false);

        if (gksudo == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken).ConfigureAwait(false))
        {
            return PrivilegeRequestResult.Granted;
        }

        // If pkexec exists but user cancels, exit code varies; treat as denied when we can.
        if (pkexec is 126 or 127 or 1)
        {
            return PrivilegeRequestResult.Denied;
        }

        return PrivilegeRequestResult.Failed;
    }

    public static string GetManualPermissionInstructions(string portPath)
    {
        if (!IsSupported)
        {
            return string.Empty;
        }

        var devicePath = ConvertToDevicePath(portPath);
        var username = Environment.UserName;

        return PrivilegeStrings.Format("LinuxSerial.Manual", devicePath, username);
    }

    private static string ConvertToDevicePath(string portPath)
    {
        if (string.IsNullOrWhiteSpace(portPath))
        {
            return portPath;
        }

        if (portPath.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return portPath;
        }

        if (!portPath.Contains('/'))
        {
            return $"/dev/{portPath}";
        }

        return portPath;
    }

    private static async Task<int?> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            // Tool missing
            return null;
        }
        catch
        {
            return -1;
        }
    }
}
