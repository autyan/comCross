using System.Runtime.InteropServices;

namespace ComCross.Shared.Helpers;

/// <summary>
/// Linux hardening: enable no_new_privs for the current process.
///
/// When enabled, the process and its children cannot gain new privileges
/// via execve (e.g., setuid binaries, file capabilities). This helps prevent
/// plugin processes from self-elevating (pkexec/sudo) even if they try.
///
/// Best-effort and Linux-only.
/// </summary>
public static class LinuxNoNewPrivs
{
    // prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0)
    private const int PrSetNoNewPrivs = 38;

    /// <summary>
    /// Tries to enable no_new_privs.
    /// Returns true if enabled (or not applicable).
    /// </summary>
    public static bool TryEnable(out string? error)
    {
        error = null;

        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            var rc = prctl(PrSetNoNewPrivs, 1, 0, 0, 0);
            if (rc == 0)
            {
                return true;
            }

            var errno = Marshal.GetLastWin32Error();
            error = $"prctl(PR_SET_NO_NEW_PRIVS) failed (errno={errno}).";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);
}
