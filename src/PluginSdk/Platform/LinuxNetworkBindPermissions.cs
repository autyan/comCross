using System.Diagnostics;
using System.Net;
using ComCross.PluginSdk.Resources;

namespace ComCross.PluginSdk.Platform;

/// <summary>
/// Linux helpers for binding privileged ports.
///
/// On Linux, binding ports &lt; 1024 typically requires root or CAP_NET_BIND_SERVICE.
/// This helper focuses on detection and actionable guidance; it does not perform persistent system changes.
/// </summary>
public static class LinuxNetworkBindPermissions
{
    private const int CapNetBindService = 10;

    public static bool IsSupported => OperatingSystem.IsLinux();

    public static bool IsPrivilegedPort(int port) => port is > 0 and < 1024;

    /// <summary>
    /// Returns true if the current process has CAP_NET_BIND_SERVICE.
    /// Best-effort: reads /proc/self/status (CapEff).
    /// </summary>
    public static bool HasCapNetBindService()
    {
        if (!IsSupported)
        {
            return true;
        }

        if (!TryReadCapEff(out var capEff))
        {
            return false;
        }

        return (capEff & (1UL << CapNetBindService)) != 0;
    }

    /// <summary>
    /// Checks whether the current process is expected to be able to bind the given ip/port.
    /// For ports &gt;= 1024 this always returns AlreadyGranted.
    /// </summary>
    public static PrivilegeRequestResult CheckCanBind(IPAddress ip, int port)
    {
        if (!IsSupported)
        {
            return PrivilegeRequestResult.AlreadyGranted;
        }

        if (port is < 1 or > 65535)
        {
            return PrivilegeRequestResult.Failed;
        }

        if (!IsPrivilegedPort(port))
        {
            return PrivilegeRequestResult.AlreadyGranted;
        }

        return HasCapNetBindService()
            ? PrivilegeRequestResult.AlreadyGranted
            : PrivilegeRequestResult.Failed;
    }

    public static string GetManualPermissionInstructions(int port)
    {
        if (!IsSupported)
        {
            return string.Empty;
        }

        if (!IsPrivilegedPort(port))
        {
            return string.Empty;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            processPath = "<path-to-ComCross.PluginHost>";
        }

        return PrivilegeStrings.Format("LinuxNetBind.Manual", processPath, Math.Max(1024, port));
    }

    private static bool TryReadCapEff(out ulong capEff)
    {
        capEff = 0;

        try
        {
            // Example line: CapEff:\t0000000000000000
            var statusPath = "/proc/self/status";
            if (!File.Exists(statusPath))
            {
                return false;
            }

            foreach (var line in File.ReadLines(statusPath))
            {
                if (!line.StartsWith("CapEff:", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    return false;
                }

                var hex = parts[1].Trim();
                if (ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, provider: null, out capEff))
                {
                    return true;
                }

                return false;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
