using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using ComCross.Platform;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Manages per-session Session Host processes.
///
/// MVP defaults:
/// - 1 session : 1 process (enforced by spawning a dedicated host per session).
/// - No compatibility layer: session lifecycle is owned by this service.
/// </summary>
public sealed class SessionHostRuntimeService
{
    private static readonly TimeSpan HostConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<SessionHostRuntimeService> _logger;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, HostGroup> _groupsByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionToGroupKey = new(StringComparer.Ordinal);

    public SessionHostRuntimeService(ILogger<SessionHostRuntimeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SessionHostRuntime? TryGet(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!_sessionToGroupKey.TryGetValue(sessionId, out var key))
        {
            return null;
        }

        if (!_groupsByKey.TryGetValue(key, out var group))
        {
            _sessionToGroupKey.TryRemove(sessionId, out _);
            return null;
        }

        if (!group.Runtime.IsAlive)
        {
            CleanupDeadGroup(key, group, reason: "dead");
            return null;
        }

        return group.Runtime;
    }

    public async Task<SessionHostRuntime> EnsureStartedAsync(PluginInfo plugin, string sessionId, CancellationToken cancellationToken = default)
        => await EnsureStartedAsync(plugin, sessionId, capabilityId: null, supportsMultiSession: false, cancellationToken);

    public async Task<SessionHostRuntime> EnsureStartedAsync(
        PluginInfo plugin,
        string sessionId,
        string? capabilityId,
        bool supportsMultiSession,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Missing sessionId.", nameof(sessionId));
        }

        var key = supportsMultiSession
            ? CreateMultiSessionKey(plugin.Manifest.Id, capabilityId)
            : CreateSingleSessionKey(sessionId);

        if (_groupsByKey.TryGetValue(key, out var existingGroup) && existingGroup.Runtime.IsAlive)
        {
            RegisterSessionToGroup(key, existingGroup, sessionId);
            return existingGroup.Runtime;
        }

        // Replace any dead runtime.
        if (existingGroup is not null)
        {
            CleanupDeadGroup(key, existingGroup, reason: "restart");
        }

        var hostPath = ResolveHostPath();
        if (hostPath is null)
        {
            throw new InvalidOperationException("Plugin host executable not found.");
        }

        var pipeName = CreatePipeName(plugin.Manifest.Id, supportsMultiSession ? capabilityId : sessionId);
        var hostToken = Guid.NewGuid().ToString("N");

        var fixedSessionId = supportsMultiSession ? null : sessionId;

        var startInfo = CreateStartInfo(hostPath, pipeName, plugin, hostToken, fixedSessionId);
        if (startInfo is null)
        {
            throw new InvalidOperationException("Unable to create session host process.");
        }

        _logger.LogInformation(
            "Starting session host: PluginId={PluginId}, SessionId={SessionId}, Mode={Mode}, CapabilityId={CapabilityId}",
            plugin.Manifest.Id,
            sessionId,
            supportsMultiSession ? "multi" : "single",
            capabilityId ?? "-");

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start session host.");
        }

        var client = new PluginHostClient(pipeName);

        // Basic readiness: ping.
        var response = await client.SendAsync(
            new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Ping),
            HostConnectTimeout);

        if (response is not { Ok: true })
        {
            client.Dispose();
            TryTerminate(process);
            throw new InvalidOperationException(response?.Error ?? "Session host failed to respond.");
        }

        var runtime = new SessionHostRuntime(plugin, sessionId, process, client);

        var group = new HostGroup(key, runtime, supportsMultiSession);
        group.AddSession(sessionId);

        if (!_groupsByKey.TryAdd(key, group))
        {
            // Race: someone else created the group. Prefer the existing group and terminate this one.
            if (_groupsByKey.TryGetValue(key, out var winner) && winner.Runtime.IsAlive)
            {
                RegisterSessionToGroup(key, winner, sessionId);

                try
                {
                    runtime.ShutdownAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                }
                catch
                {
                }
                runtime.Dispose();

                return winner.Runtime;
            }

            // Existing group is dead/missing; replace.
            _groupsByKey[key] = group;
        }

        RegisterSessionToGroup(key, group, sessionId);
        return runtime;
    }

    public async Task StopAsync(string sessionId, TimeSpan timeout, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (!_sessionToGroupKey.TryRemove(sessionId, out var key))
        {
            return;
        }

        if (!_groupsByKey.TryGetValue(key, out var group))
        {
            return;
        }

        var shouldStop = false;
        lock (_gate)
        {
            group.RemoveSession(sessionId);
            shouldStop = group.SessionCount == 0;
        }

        if (!shouldStop)
        {
            return;
        }

        if (!_groupsByKey.TryRemove(key, out var removed))
        {
            return;
        }

        try
        {
            await removed.Runtime.ShutdownAsync(timeout);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Session host shutdown failed: Key={Key}, SessionId={SessionId}, Reason={Reason}", key, sessionId, reason ?? "-");
        }
        finally
        {
            removed.Runtime.Dispose();
        }
    }

    private void RegisterSessionToGroup(string key, HostGroup group, string sessionId)
    {
        lock (_gate)
        {
            group.AddSession(sessionId);
            _sessionToGroupKey[sessionId] = key;
        }
    }

    private void CleanupDeadGroup(string key, HostGroup group, string reason)
    {
        _groupsByKey.TryRemove(key, out _);
        string[] sessions;
        lock (_gate)
        {
            sessions = group.GetSessionsSnapshot();
        }

        foreach (var sessionId in sessions)
        {
            _sessionToGroupKey.TryRemove(sessionId, out _);
        }

        try
        {
            group.Runtime.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposed old session host runtime failed: Key={Key}, Reason={Reason}", key, reason);
        }
    }

    private static string CreateSingleSessionKey(string sessionId) => $"session:{sessionId}";

    private static string CreateMultiSessionKey(string pluginId, string? capabilityId)
    {
        var cap = string.IsNullOrWhiteSpace(capabilityId) ? "cap" : capabilityId;
        return $"multi:{pluginId}:{cap}";
    }

    private static string CreatePipeName(string pluginId, string? discriminator)
    {
        // On Unix, NamedPipeServerStream uses a Unix domain socket under /tmp/CoreFxPipe_{pipeName}.
        // The full socket path must be <= 108 chars, so pipeName must be kept short.
        var safePlugin = string.Concat(pluginId.Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-'));
        if (string.IsNullOrWhiteSpace(safePlugin))
        {
            safePlugin = "plugin";
        }

        // Keep some human-readable context but bound length tightly.
        const int maxPluginLen = 12;
        if (safePlugin.Length > maxPluginLen)
        {
            safePlugin = safePlugin[..maxPluginLen];
        }

        var discriminatorValue = discriminator ?? string.Empty;
        var hashInput = $"{pluginId}|{discriminatorValue}";
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hashInput));
        var hashHex = Convert.ToHexString(hashBytes.AsSpan(0, 10)).ToLowerInvariant(); // 20 chars
        var nonce = Guid.NewGuid().ToString("N")[..8];

        return $"ccsh-{safePlugin}-{hashHex}-{nonce}";
    }

    private static ProcessStartInfo? CreateStartInfo(string hostPath, string pipeName, PluginInfo plugin, string hostToken, string? fixedSessionId)
    {
        if (string.IsNullOrWhiteSpace(hostPath))
        {
            return null;
        }

        var parent = Process.GetCurrentProcess();
        var parentPid = parent.Id;
        string? parentStartUtc;
        try
        {
            parentStartUtc = parent.StartTime.ToUniversalTime().ToString("O");
        }
        catch
        {
            parentStartUtc = null;
        }

        var args =
            $"--pipe \"{pipeName}\"" +
            $" --plugin \"{plugin.AssemblyPath}\"" +
            $" --entry \"{plugin.Manifest.EntryPoint}\"" +
            $" --host-token \"{hostToken}\"" +
            $" --role session" +
            $" --parent-pid {parentPid}";

        if (!string.IsNullOrWhiteSpace(fixedSessionId))
        {
            args += $" --session-id \"{fixedSessionId}\"";
        }

        if (!string.IsNullOrWhiteSpace(parentStartUtc))
        {
            args += $" --parent-start-utc \"{parentStartUtc}\"";
        }

        if (hostPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo("dotnet", $"\"{hostPath}\" {args}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        return new ProcessStartInfo(hostPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }

    private static string? ResolveHostPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var exeName = PlatformInfo.SessionHostExecutableName;
        var exePath = Path.Combine(baseDir, exeName);
        if (File.Exists(exePath))
        {
            return exePath;
        }

        var dllPath = Path.Combine(baseDir, "ComCross.SessionHost.dll");
        return File.Exists(dllPath) ? dllPath : null;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed class HostGroup
    {
        private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);

        public HostGroup(string key, SessionHostRuntime runtime, bool isShared)
        {
            Key = key;
            Runtime = runtime;
            IsShared = isShared;
        }

        public string Key { get; }
        public SessionHostRuntime Runtime { get; }
        public bool IsShared { get; }

        public int SessionCount => _sessions.Count;

        public void AddSession(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessions.Add(sessionId);
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessions.Remove(sessionId);
            }
        }

        public string[] GetSessionsSnapshot() => _sessions.ToArray();
    }
}

public sealed class SessionHostRuntime : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    public SessionHostRuntime(PluginInfo plugin, string? sessionId, Process process, PluginHostClient client)
    {
        Plugin = plugin;
        SessionId = sessionId;
        Process = process;
        Client = client;
    }

    public PluginInfo Plugin { get; }
    public string? SessionId { get; }
    public Process Process { get; }
    public PluginHostClient Client { get; }

    public bool IsAlive => !Process.HasExited;

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        if (Process.HasExited)
        {
            return;
        }

        try
        {
            var requestTimeout = timeout <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : (timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1));

            _ = await Client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Shutdown, SessionId: SessionId),
                requestTimeout);
        }
        catch
        {
        }

        try
        {
            if (timeout > TimeSpan.Zero)
            {
                using var cts = new CancellationTokenSource(timeout);
                await Process.WaitForExitAsync(cts.Token);
            }
        }
        catch
        {
        }

        if (!Process.HasExited)
        {
            try
            {
                Process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Client.Dispose();
        }
        catch
        {
        }

        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            Process.Dispose();
        }
        catch
        {
        }
    }
}
