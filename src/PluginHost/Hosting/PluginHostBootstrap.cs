using System.Diagnostics;

namespace ComCross.PluginHost.Hosting;

internal static class PluginHostBootstrap
{
    public static bool TryParse(string[] args, out PluginHostOptions options, out string error)
    {
        var argsMap = ParseArgs(args);

        if (!argsMap.TryGetValue("--pipe", out var pipeName) ||
            !argsMap.TryGetValue("--plugin", out var pluginPath) ||
            !argsMap.TryGetValue("--entry", out var entryPoint))
        {
            options = default!;
            error = "Missing required arguments: --pipe --plugin --entry";
            return false;
        }

        argsMap.TryGetValue("--role", out var roleRaw);
        var role = NormalizeRole(roleRaw,
#if SESSION_HOST
            defaultRole: "session"
#else
            defaultRole: "ui"
#endif
        );

#if SESSION_HOST
        if (!string.Equals(role, "session", StringComparison.Ordinal))
        {
            options = default!;
            error = "This executable is Session Host only (role=session).";
            return false;
        }
#else
        if (!string.Equals(role, "ui", StringComparison.Ordinal))
        {
            options = default!;
            error = "This executable is UI Host only (role=ui).";
            return false;
        }
#endif

        argsMap.TryGetValue("--plugin-id", out var pluginIdRaw);
        var pluginId = string.IsNullOrWhiteSpace(pluginIdRaw)
            ? Path.GetFileNameWithoutExtension(pluginPath)
            : pluginIdRaw.Trim();

        argsMap.TryGetValue("--session-id", out var fixedSessionId);
#if !SESSION_HOST
        fixedSessionId = null;
#endif

        argsMap.TryGetValue("--event-pipe", out var eventPipeName);
        argsMap.TryGetValue("--host-token", out var hostToken);
        argsMap.TryGetValue("--instance-id", out var instanceId);

        argsMap.TryGetValue("--log-dir", out var logDir);
        argsMap.TryGetValue("--log-format", out var logFormat);
        argsMap.TryGetValue("--log-min-level", out var logMinLevel);

        options = new PluginHostOptions(
            PipeName: pipeName,
            PluginPath: pluginPath,
            EntryPoint: entryPoint,
            Role: role,
            PluginId: pluginId,
            InstanceId: string.IsNullOrWhiteSpace(instanceId) ? null : instanceId,
            FixedSessionId: string.IsNullOrWhiteSpace(fixedSessionId) ? null : fixedSessionId,
            EventPipeName: string.IsNullOrWhiteSpace(eventPipeName) ? null : eventPipeName,
            HostToken: string.IsNullOrWhiteSpace(hostToken) ? null : hostToken,
            LogDir: logDir,
            LogFormat: logFormat,
            LogMinLevel: logMinLevel);

        error = string.Empty;
        return true;
    }

    public static Task? StartParentMonitorIfRequested(string[] args)
        => StartParentMonitorIfRequested(ParseArgs(args));

    public static Task? StartParentMonitorIfRequested(Dictionary<string, string> argsMap)
    {
        if (!argsMap.TryGetValue("--parent-pid", out var pidText) ||
            !int.TryParse(pidText, out var parentPid) ||
            parentPid <= 0)
        {
            return null;
        }

        DateTimeOffset? expectedStartUtc = null;
        if (argsMap.TryGetValue("--parent-start-utc", out var startText) &&
            DateTimeOffset.TryParse(startText, out var parsed))
        {
            expectedStartUtc = parsed;
        }

        return Task.Run(async () =>
        {
            try
            {
                var parent = Process.GetProcessById(parentPid);

                if (expectedStartUtc is not null)
                {
                    try
                    {
                        var actual = parent.StartTime.ToUniversalTime();
                        var delta = (actual - expectedStartUtc.Value.UtcDateTime).Duration();
                        if (delta > TimeSpan.FromSeconds(2))
                        {
                            Environment.Exit(0);
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                await parent.WaitForExitAsync();
            }
            catch
            {
            }

            Environment.Exit(0);
        });
    }

    private static string NormalizeRole(string? role, string defaultRole)
        => string.IsNullOrWhiteSpace(role) ? defaultRole : role.Trim().ToLowerInvariant();

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            map[args[i]] = args[i + 1];
            i++;
        }

        return map;
    }
}
