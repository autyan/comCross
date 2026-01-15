using System.Diagnostics;
using System.Runtime.InteropServices;
using ComCross.Shared.Interfaces;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Linux-specific serial port access manager
/// Checks actual device permissions and provides temporary elevation without modifying user groups
/// </summary>
public sealed class LinuxSerialPortAccessManager : ISerialPortAccessManager
{
    public async Task<bool> HasAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        if (string.IsNullOrEmpty(portPath))
        {
            return false;
        }

        // Convert Windows-style port names to Linux device paths if needed
        var devicePath = ConvertToDevicePath(portPath);
        
        if (!File.Exists(devicePath))
        {
            return false;
        }

        try
        {
            // Try to open the device file to check actual access permission
            using var fileStream = File.Open(devicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            // Device might be busy, but we have permission
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PermissionRequestResult> RequestAccessPermissionAsync(string portPath, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return PermissionRequestResult.AlreadyGranted;
        }

        if (string.IsNullOrEmpty(portPath))
        {
            return PermissionRequestResult.Failed;
        }

        var devicePath = ConvertToDevicePath(portPath);

        // First check if already has permission
        if (await HasAccessPermissionAsync(portPath, cancellationToken))
        {
            return PermissionRequestResult.AlreadyGranted;
        }

        if (!File.Exists(devicePath))
        {
            return PermissionRequestResult.Failed;
        }

        // Try pkexec to temporarily change device permissions (chmod 666)
        // This is temporary and will be reset on device reconnect or reboot
        if (await TryPkexecChmodAsync(devicePath, cancellationToken))
        {
            return PermissionRequestResult.Granted;
        }

        // Try gksu/gksudo (older GUI method)
        if (await TryGksuChmodAsync(devicePath, cancellationToken))
        {
            return PermissionRequestResult.Granted;
        }

        // All automatic methods failed
        return PermissionRequestResult.Failed;
    }

    public string GetManualPermissionInstructions(string portPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return string.Empty;
        }

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
        // If already a device path, return as-is
        if (portPath.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return portPath;
        }

        // Convert simple names like "ttyUSB0" to "/dev/ttyUSB0"
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
            // Create a shell script to avoid pkexec argument parsing issues
            var scriptPath = Path.Combine(Path.GetTempPath(), $"comcross-chmod-{Guid.NewGuid():N}.sh");
            await File.WriteAllTextAsync(scriptPath, $"#!/bin/sh\nchmod 666 {devicePath}\n", cancellationToken);
            
            // Make script executable
            var chmodResult = await RunCommandWithExitCodeAsync("chmod", $"+x {scriptPath}", cancellationToken);
            if (chmodResult != 0)
            {
                File.Delete(scriptPath);
                return false;
            }

            try
            {
                // Use pkexec to run the script
                var exitCode = await RunCommandWithExitCodeAsync(
                    "pkexec",
                    scriptPath,
                    cancellationToken);

                if (exitCode != 0)
                {
                    return false;
                }

                // Verify permission was granted
                var result = await HasAccessPermissionAsync(devicePath, cancellationToken);
                return result;
            }
            finally
            {
                // Clean up script
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to request permission via pkexec: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryGksuChmodAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            // Try gksu first
            var exitCode = await RunCommandWithExitCodeAsync(
                "gksu",
                $"chmod 666 {devicePath}",
                cancellationToken);

            if (exitCode == 0 && await HasAccessPermissionAsync(devicePath, cancellationToken))
            {
                return true;
            }

            // Try gksudo
            exitCode = await RunCommandWithExitCodeAsync(
                "gksudo",
                $"chmod 666 {devicePath}",
                cancellationToken);

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

    private static async Task<int> RunCommandWithExitCodeAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false // Allow GUI dialog for pkexec
                }
            };

            process.Start();
            
            // Read output to prevent deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            
            await process.WaitForExitAsync(cancellationToken);
            
            // Log any error output for debugging
            var error = await errorTask;
            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine($"Command {command} error output: {error}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to run command {command}: {ex.Message}");
            return -1;
        }
    }
}
