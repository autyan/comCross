using System.Diagnostics;
using ComCross.Platform;
using ComCross.Platform.SingleInstance;
using ComCross.Platform.UserDirectories;

namespace ComCross.Startup;

internal sealed class StartupLauncher
{
    private readonly IReadOnlyList<string> _args;

    public StartupLauncher(IReadOnlyList<string> args)
    {
        _args = args;
    }

    public async Task<StartupLaunchResult> RunAsync()
    {
        var installDirectory = AppContext.BaseDirectory;
        var resolver = new StartupInstanceResolver();
        var instance = resolver.Resolve(installDirectory, _args);
        var paths = StartupPaths.Create(instance, new PlatformUserDirectoryProvider());
        var logger = new StartupLogWriter(paths.StartupLogDirectory);

        try
        {
            await logger.WriteLineAsync($"Startup begin. InstallDirectory={installDirectory}");
            await logger.WriteLineAsync($"InstanceId={instance.InstanceId}; Channel={instance.Channel}; ManifestPath={instance.ManifestPath ?? "-"}");

            if (!await TryPassSingleInstancePreflightAsync(instance, paths, logger))
            {
                return StartupLaunchResult.Failed(
                    "ComCross is already running",
                    "ComCross is already running for this channel.",
                    paths.StartupLogDirectory);
            }

            var shellPath = ResolveShellPath(installDirectory);
            await logger.WriteLineAsync($"Resolved ShellPath={shellPath}");
            if (string.IsNullOrWhiteSpace(shellPath) || !File.Exists(shellPath))
            {
                return StartupLaunchResult.Failed(
                    "ComCross cannot start",
                    $"Shell executable was not found: {shellPath}",
                    paths.StartupLogDirectory);
            }

            var shellArgs = string.IsNullOrWhiteSpace(instance.ManifestPath)
                ? string.Empty
                : $"--instance-manifest \"{instance.ManifestPath}\"";

            var startInfo = new ProcessStartInfo(shellPath, shellArgs)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(shellPath) ?? installDirectory
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                await logger.WriteLineAsync("Shell process start returned null.");
                return StartupLaunchResult.Failed(
                    "ComCross cannot start",
                    "Shell process could not be started.",
                    paths.StartupLogDirectory);
            }

            await logger.WriteLineAsync("Shell start requested successfully.");
            return StartupLaunchResult.Success(paths.StartupLogDirectory);
        }
        catch (Exception ex)
        {
            await logger.WriteLineAsync($"Startup failed: {ex}");
            return StartupLaunchResult.Failed(
                "ComCross cannot start",
                ex.Message,
                paths.StartupLogDirectory);
        }
    }

    private static async Task<bool> TryPassSingleInstancePreflightAsync(
        StartupInstanceIdentity instance,
        StartupPaths paths,
        StartupLogWriter logger)
    {
        using var instanceLock = new PlatformSingleInstanceLock().TryAcquire(
            instance.InstanceId,
            paths.LocalDataDirectory,
            out var lockError);

        if (instanceLock is not null)
        {
            await logger.WriteLineAsync("Single-instance preflight passed. Shell will acquire the long-lived application lock.");
            return true;
        }

        await logger.WriteLineAsync($"Single-instance preflight failed: {lockError}");
        return false;
    }

    private string ResolveShellPath(string installDirectory)
    {
        var explicitPath = TryGetOption("--shell-path");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var envPath = Environment.GetEnvironmentVariable("COMCROSS_SHELL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        return Path.Combine(installDirectory, PlatformInfo.ShellExecutableName);
    }

    private string? TryGetOption(string optionName)
    {
        for (var i = 0; i < _args.Count; i++)
        {
            if (!string.Equals(_args[i], optionName, StringComparison.Ordinal))
            {
                continue;
            }

            return i + 1 < _args.Count ? _args[i + 1] : null;
        }

        return null;
    }
}

internal sealed record StartupLaunchResult(bool Ok, string Title, string Message, string LogDirectory)
{
    public static StartupLaunchResult Success(string logDirectory)
        => new(true, string.Empty, string.Empty, logDirectory);

    public static StartupLaunchResult Failed(string title, string message, string logDirectory)
        => new(false, title, message, logDirectory);
}
