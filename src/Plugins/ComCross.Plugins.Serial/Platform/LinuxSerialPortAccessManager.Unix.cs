using System.Diagnostics;

namespace ComCross.Plugins.Serial.Platform;

/// <summary>
/// Linux/Unix serial port access manager.
/// </summary>
public sealed class LinuxSerialPortAccessManager : ISerialPortAccessManager
{
    public async Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(portPath))
        {
            return false;
        }

        var devicePath = ConvertToDevicePath(portPath);
        if (!File.Exists(devicePath))
        {
            return false;
        }

        try
        {
            using var _ = File.Open(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(portPath))
        {
            return PermissionRequestResult.Failed;
        }

        var devicePath = ConvertToDevicePath(portPath);

        if (await HasAccessPermissionAsync(portPath, cancellationToken))
        {
            return PermissionRequestResult.AlreadyGranted;
        }

        if (!File.Exists(devicePath))
        {
            return PermissionRequestResult.Failed;
        }

        if (await TryPkexecChmodAsync(devicePath, cancellationToken))
        {
            return PermissionRequestResult.Granted;
        }

        if (await TryGksuChmodAsync(devicePath, cancellationToken))
        {
            return PermissionRequestResult.Granted;
        }

        return PermissionRequestResult.Failed;
    }

    public string GetManualPermissionInstructions(string portPath)
    {
        var devicePath = ConvertToDevicePath(portPath);
        var username = Environment.UserName;

        return $"To access the serial port {devicePath}, you have several options:\n\n" +
               $"Option 1 - Temporary access (until device is reconnected):\n" +
               $"  sudo chmod 666 {devicePath}\n\n" +
               $"Option 2 - Permanent access (recommended):\n" +
               $"  sudo usermod -aG dialout {username}\n" +
               $"  Then log out and log back in\n\n" +
               $"Option 3 - Automatic via udev rule:\n" +
               $"  Create /etc/udev/rules.d/50-serial.rules:\n" +
               $"  KERNEL==\"ttyUSB[0-9]*\", MODE=\"0666\"\n" +
               $"  KERNEL==\"ttyACM[0-9]*\", MODE=\"0666\"\n" +
               $"  Then run: sudo udevadm control --reload-rules";
    }

    private static string ConvertToDevicePath(string portPath)
    {
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

    private async Task<bool> TryPkexecChmodAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"comcross-chmod-{Guid.NewGuid():N}.sh");
            await File.WriteAllTextAsync(scriptPath, $"#!/bin/sh\nchmod 666 {devicePath}\n", cancellationToken);

            var chmodResult = await RunCommandWithExitCodeAsync("chmod", $"+x {scriptPath}", cancellationToken);
            if (chmodResult != 0)
            {
                try { File.Delete(scriptPath); } catch { }
                return false;
            }

            try
            {
                var exitCode = await RunCommandWithExitCodeAsync("pkexec", scriptPath, cancellationToken);
                if (exitCode != 0)
                {
                    return false;
                }

                return await HasAccessPermissionAsync(devicePath, cancellationToken);
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryGksuChmodAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            var exitCode = await RunCommandWithExitCodeAsync("gksu", $"chmod 666 {devicePath}", cancellationToken);
            if (exitCode == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken))
            {
                return true;
            }

            exitCode = await RunCommandWithExitCodeAsync("gksudo", $"chmod 666 {devicePath}", cancellationToken);
            if (exitCode == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunCommandWithExitCodeAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return -1;
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
