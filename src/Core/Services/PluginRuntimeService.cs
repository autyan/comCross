using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using ComCross.Platform;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class PluginRuntimeService
{
    private static readonly TimeSpan HostConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<PluginRuntime, byte> _exitHandled = new();

    private readonly ILogger<PluginRuntimeService> _logger;

    private readonly SharedMemorySessionService _sharedMemorySessionService;

    public PluginRuntimeService(SharedMemorySessionService sharedMemorySessionService, ILogger<PluginRuntimeService> logger)
    {
        _sharedMemorySessionService = sharedMemorySessionService ?? throw new ArgumentNullException(nameof(sharedMemorySessionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Action<PluginRuntime, PluginHostEvent>? PluginEventReceived;

    public IReadOnlyList<PluginRuntime> LoadPlugins(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlyDictionary<string, bool> enabledMap)
    {
        var runtimes = new List<PluginRuntime>();

        foreach (var plugin in plugins)
        {
            var enabled = enabledMap.TryGetValue(plugin.Manifest.Id, out var isEnabled) ? isEnabled : true;
            if (!enabled)
            {
                runtimes.Add(PluginRuntime.Disabled(plugin));
                continue;
            }

            runtimes.Add(StartHost(plugin));
        }

        return runtimes;
    }

    public async Task ShutdownAsync(
        IReadOnlyList<PluginRuntime> runtimes,
        TimeSpan timeoutPerHost,
        Action<PluginRuntime, Exception?>? onError = null)
    {
        if (runtimes.Count == 0)
        {
            return;
        }

        var tasks = runtimes.Select(runtime => ShutdownOneAsync(runtime, timeoutPerHost, onError));
        await Task.WhenAll(tasks);
    }

    public Task NotifyLanguageChangedAsync(
        IReadOnlyList<PluginRuntime> runtimes,
        string cultureCode,
        Action<PluginRuntime, Exception, bool>? onError = null,
        CancellationToken cancellationToken = default)
        => NotifyAsync(runtimes, PluginNotification.LanguageChanged(cultureCode), onError, cancellationToken);

    public async Task NotifyAsync(
        IReadOnlyList<PluginRuntime> runtimes,
        PluginNotification notification,
        Action<PluginRuntime, Exception, bool>? onError = null,
        CancellationToken cancellationToken = default)
    {
        notification.ValidateGlobal();

        foreach (var runtime in runtimes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (runtime.State != PluginLoadState.Loaded)
            {
                continue;
            }

            if (runtime.Process is { HasExited: true })
            {
                var restarted = RestartHost(runtime);
                onError?.Invoke(runtime, new InvalidOperationException("Plugin host exited."), restarted);
                continue;
            }

            PluginHostResponse? response = null;
            try
            {
                response = await runtime.Client?.SendAsync(
                    new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Notify, null, notification),
                    RequestTimeout)!;
            }
            catch (Exception ex)
            {
                var recovered = RestartHost(runtime);
                onError?.Invoke(runtime, ex, recovered);
                continue;
            }

            if (response is { Ok: true })
            {
                continue;
            }

            var error = response?.Error ?? "Plugin host unavailable.";
            var recoveredOnError = RestartHost(runtime);
            onError?.Invoke(runtime, new InvalidOperationException(error), recoveredOnError);
        }
    }

    private async Task ShutdownOneAsync(
        PluginRuntime runtime,
        TimeSpan timeout,
        Action<PluginRuntime, Exception?>? onError)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return;
        }

        var process = runtime.Process;
        if (process is null || process.HasExited)
        {
            return;
        }

        // Deterministic cleanup: don't leak shared-memory segments/readers across shutdown.
        CleanupRuntimeSessionBestEffort(runtime, "shutdown");

        try
        {
            var requestTimeout = timeout <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : (timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1));

            _ = await runtime.Client?.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Shutdown, null),
                requestTimeout)!;
        }
        catch (Exception ex)
        {
            onError?.Invoke(runtime, ex);
        }

        try
        {
            if (timeout > TimeSpan.Zero)
            {
                using var cts = new CancellationTokenSource(timeout);
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(runtime, ex);
        }
    }

    private PluginRuntime StartHost(PluginInfo plugin)
    {
        _logger.LogInformation("Starting plugin host: {PluginId}", plugin.Manifest.Id);
        var runtime = new PluginRuntime(plugin);
        return StartHost(runtime) ? runtime : runtime;
    }

    private bool RestartHost(PluginRuntime runtime)
    {
        _logger.LogWarning("Restarting plugin host: {PluginId}", runtime.Info.Manifest.Id);
        CleanupRuntimeSessionBestEffort(runtime, "restart-host");
        runtime.DisposeHost();
        var started = StartHost(runtime);
        if (started)
        {
            _logger.LogInformation("Plugin host restarted: {PluginId}", runtime.Info.Manifest.Id);
        }
        else
        {
            _logger.LogError("Plugin host restart failed: {PluginId}, Error={Error}", runtime.Info.Manifest.Id, runtime.Error);
        }

        return started;
    }

    private void CleanupRuntimeSessionBestEffort(PluginRuntime runtime, string reason)
    {
        var sessionIds = runtime.GetOpenSessionIdsSnapshot();
        if (sessionIds.Count == 0)
        {
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            _logger.LogInformation(
                "Cleaning up runtime session (best-effort): {PluginId}, SessionId={SessionId}, Reason={Reason}",
                runtime.Info.Manifest.Id,
                sessionId,
                reason);

            try
            {
                runtime.EndSession(sessionId);
            }
            catch
            {
            }

            try
            {
                // Deterministic cleanup for leaked segments/readers when host crashes or is restarted.
                _sharedMemorySessionService.CleanupAsync(sessionId).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
    }

    private bool StartHost(PluginRuntime runtime)
    {
        var pipeName = CreatePipeName(runtime.Info.Manifest.Id);
        var eventPipeName = pipeName + "-events";
        var hostToken = Guid.NewGuid().ToString("N");
        var hostPath = ResolveHostPath();
        if (hostPath is null)
        {
            runtime.SetFailed("Plugin host executable not found.");
            return false;
        }

        var processStart = CreateStartInfo(hostPath, pipeName, eventPipeName, runtime.Info, hostToken);
        if (processStart is null)
        {
            runtime.SetFailed("Unable to create plugin host process.");
            return false;
        }

        try
        {
            runtime.SetHostToken(hostToken);

            var process = Process.Start(processStart);
            if (process is null)
            {
                _logger.LogWarning("Failed to start plugin host process: {PluginId}", runtime.Info.Manifest.Id);
                runtime.SetFailed("Failed to start plugin host.");
                return false;
            }

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnHostExited(runtime);
            }
            catch
            {
                // best-effort
            }

            var client = new PluginHostClient(pipeName);
            var eventClient = new PluginHostEventClient(eventPipeName);
            eventClient.EventReceived += evt =>
            {
                try
                {
                    HandleHostEvent(runtime, evt);
                }
                catch
                {
                }

                if (ShouldPublishEvent(runtime, evt))
                {
                    PluginEventReceived?.Invoke(runtime, evt);
                }
            };
            eventClient.Start();

            var response = client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Ping),
                HostConnectTimeout).GetAwaiter().GetResult();

            if (response is not { Ok: true })
            {
                var error = response?.Error ?? "Plugin host failed to respond.";
                _logger.LogWarning(
                    "Plugin host ping failed: {PluginId}, Error={Error}",
                    runtime.Info.Manifest.Id,
                    error);
                runtime.SetFailed(error);
                client.Dispose();
                eventClient.Dispose();
                TryTerminate(process);
                return false;
            }

            // Wait briefly for host registration handshake (best-effort but required for binding correctness).
            var registeredDeadline = DateTimeOffset.UtcNow + HostConnectTimeout;
            while (!runtime.IsHostRegistered && DateTimeOffset.UtcNow < registeredDeadline)
            {
                Thread.Sleep(30);
            }

            if (!runtime.IsHostRegistered)
            {
                _logger.LogWarning(
                    "Plugin host registration handshake timed out: {PluginId}",
                    runtime.Info.Manifest.Id);
                runtime.SetFailed("Plugin host registration handshake timed out.");
                client.Dispose();
                eventClient.Dispose();
                TryTerminate(process);
                return false;
            }

            var (capabilities, capabilitiesError) = TryGetCapabilities(client);
            runtime.SetLoaded(process, client, eventClient, pipeName, eventPipeName, hostToken, capabilities, capabilitiesError);

            _logger.LogInformation(
                "Plugin host loaded: {PluginId}, Pid={Pid}, Capabilities={Count}",
                runtime.Info.Manifest.Id,
                process.Id,
                capabilities.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin host start failed: {PluginId}", runtime.Info.Manifest.Id);
            runtime.SetFailed(ex.Message);
            return false;
        }
    }

    private void OnHostExited(PluginRuntime runtime)
    {
        // Ensure we only handle exit once per runtime instance.
        if (!_exitHandled.TryAdd(runtime, 0))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogWarning("Plugin host exited: {PluginId}", runtime.Info.Manifest.Id);
                CleanupRuntimeSessionBestEffort(runtime, "host-exited");
            }
            catch
            {
            }

            try
            {
                if (runtime.State == PluginLoadState.Loaded)
                {
                    runtime.SetFailed("Plugin host exited.");
                }
            }
            catch
            {
            }
        });
    }

    private static void HandleHostEvent(PluginRuntime runtime, PluginHostEvent evt)
    {
        if (evt.Payload is null)
        {
            return;
        }

        if (string.Equals(evt.Type, PluginHostEventTypes.HostRegistered, StringComparison.Ordinal))
        {
            PluginHostRegisteredEvent? payload;
            try
            {
                payload = evt.Payload.Value.Deserialize<PluginHostRegisteredEvent>(JsonOptions);
            }
            catch
            {
                payload = null;
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
            {
                return;
            }

            runtime.TryMarkHostRegistered(payload.Token, payload.ProcessId);
            return;
        }

        if (string.Equals(evt.Type, PluginHostEventTypes.SessionRegistered, StringComparison.Ordinal))
        {
            PluginHostSessionRegisteredEvent? payload;
            try
            {
                payload = evt.Payload.Value.Deserialize<PluginHostSessionRegisteredEvent>(JsonOptions);
            }
            catch
            {
                payload = null;
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Token) || string.IsNullOrWhiteSpace(payload.SessionId))
            {
                return;
            }

            runtime.TryMarkSessionRegistered(payload.Token, payload.ProcessId, payload.SessionId);
        }
    }

    private static bool ShouldPublishEvent(PluginRuntime runtime, PluginHostEvent evt)
    {
        if (string.Equals(evt.Type, PluginHostEventTypes.UiStateInvalidated, StringComparison.Ordinal))
        {
            if (evt.Payload is null)
            {
                return false;
            }

            try
            {
                var invalidated = evt.Payload.Value.Deserialize<PluginHostUiStateInvalidatedEvent>(JsonOptions);
                if (invalidated is null || string.IsNullOrWhiteSpace(invalidated.CapabilityId))
                {
                    return false;
                }

                // ADR-010 closure: sessionless is represented by null only.
                if (invalidated.SessionId is not null && string.IsNullOrWhiteSpace(invalidated.SessionId))
                {
                    return false;
                }

                if (invalidated.SessionId is not null)
                {
                    if (!runtime.IsSessionOpen(invalidated.SessionId))
                    {
                        return false;
                    }

                    if (!runtime.IsSessionRegistered(invalidated.SessionId))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static (IReadOnlyList<PluginCapabilityDescriptor> Capabilities, string? Error) TryGetCapabilities(PluginHostClient client)
    {
        try
        {
            var response = client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetCapabilities),
                RequestTimeout).GetAwaiter().GetResult();

            if (response is not { Ok: true })
            {
                return (Array.Empty<PluginCapabilityDescriptor>(), response?.Error ?? "Plugin host unavailable.");
            }

            if (response.Payload is null)
            {
                return (Array.Empty<PluginCapabilityDescriptor>(), null);
            }

            List<PluginCapabilityDescriptor>? list = response.Payload.Value.Deserialize<List<PluginCapabilityDescriptor>>(JsonOptions);
            IReadOnlyList<PluginCapabilityDescriptor> capabilities = list is null
                ? Array.Empty<PluginCapabilityDescriptor>()
                : list;
            return (capabilities, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<PluginCapabilityDescriptor>(), ex.Message);
        }
    }

    private static ProcessStartInfo? CreateStartInfo(string hostPath, string pipeName, string eventPipeName, PluginInfo plugin, string hostToken)
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

        var args = $"--pipe \"{pipeName}\" --event-pipe \"{eventPipeName}\" --plugin \"{plugin.AssemblyPath}\" --entry \"{plugin.Manifest.EntryPoint}\" --host-token \"{hostToken}\" --parent-pid {parentPid}";
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
        var exeName = PlatformInfo.PluginHostExecutableName;
        var exePath = Path.Combine(baseDir, exeName);
        if (File.Exists(exePath))
        {
            return exePath;
        }

        var dllPath = Path.Combine(baseDir, "ComCross.PluginHost.dll");
        return File.Exists(dllPath) ? dllPath : null;
    }

    private static string CreatePipeName(string pluginId)
    {
        var safe = string.Concat(pluginId.Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-'));
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "plugin";
        }

        return $"comcross-{safe}-{Guid.NewGuid():N}";
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
}

public sealed class PluginRuntime
{
    public PluginRuntime(PluginInfo info)
    {
        Info = info;
        State = PluginLoadState.Failed;
    }

    private PluginRuntime(PluginInfo info, PluginLoadState state)
    {
        Info = info;
        State = state;
    }

    public PluginInfo Info { get; }
    public PluginLoadState State { get; private set; }
    public Process? Process { get; private set; }
    public PluginHostClient? Client { get; private set; }
    public PluginHostEventClient? EventClient { get; private set; }
    public string? PipeName { get; private set; }
    public string? EventPipeName { get; private set; }
    public string? HostToken { get; private set; }
    public bool IsHostRegistered { get; private set; }
    public int? HostProcessId { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<PluginCapabilityDescriptor> Capabilities { get; private set; } = Array.Empty<PluginCapabilityDescriptor>();
    public string? CapabilitiesError { get; private set; }

    private readonly object _sessionSync = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _sessionRegistrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openSessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IDisposable>> _sessionDisposables = new(StringComparer.Ordinal);

    public IReadOnlyList<string> GetOpenSessionIdsSnapshot()
    {
        lock (_sessionSync)
        {
            return _openSessions.ToArray();
        }
    }

    public void RegisterSessionDisposable(string sessionId, IDisposable disposable)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Missing sessionId.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(disposable);

        lock (_sessionSync)
        {
            if (!_sessionDisposables.TryGetValue(sessionId, out var list))
            {
                list = new List<IDisposable>();
                _sessionDisposables[sessionId] = list;
            }

            list.Add(disposable);
        }
    }

    private void DisposeSessionResourcesLocked(string sessionId)
    {
        if (!_sessionDisposables.TryGetValue(sessionId, out var list))
        {
            return;
        }

        _sessionDisposables.Remove(sessionId);
        foreach (var d in list)
        {
            try
            {
                d.Dispose();
            }
            catch
            {
            }
        }
    }

    private void DisposeAllSessionResourcesLocked()
    {
        if (_sessionDisposables.Count == 0)
        {
            return;
        }

        var keys = _sessionDisposables.Keys.ToArray();
        foreach (var sessionId in keys)
        {
            DisposeSessionResourcesLocked(sessionId);
        }
    }

    private void FailAllPendingSessionRegistrationsLocked()
    {
        foreach (var pending in _sessionRegistrations.Values)
        {
            try
            {
                pending.TrySetResult(false);
            }
            catch
            {
            }
        }

        _sessionRegistrations.Clear();
    }

    public static PluginRuntime Disabled(PluginInfo info)
    {
        return new PluginRuntime(info, PluginLoadState.Disabled);
    }

    public void SetHostToken(string hostToken)
    {
        HostToken = hostToken;
        IsHostRegistered = false;
        HostProcessId = null;

        lock (_sessionSync)
        {
            FailAllPendingSessionRegistrationsLocked();
            _openSessions.Clear();
            _activeSessions.Clear();
            DisposeAllSessionResourcesLocked();
        }
    }

    public void BeginSessionRegistration(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionSync)
        {
            _openSessions.Add(sessionId);
            _activeSessions.Remove(sessionId);

            if (_sessionRegistrations.TryGetValue(sessionId, out var existing))
            {
                try
                {
                    existing.TrySetResult(false);
                }
                catch
                {
                }
            }

            _sessionRegistrations[sessionId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public bool IsSessionOpen(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        lock (_sessionSync)
        {
            return _openSessions.Contains(sessionId);
        }
    }

    public bool IsSessionRegistered(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        lock (_sessionSync)
        {
            return _activeSessions.Contains(sessionId);
        }
    }

    public void EndSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionSync)
        {
            _openSessions.Remove(sessionId);
            _activeSessions.Remove(sessionId);
            DisposeSessionResourcesLocked(sessionId);
            if (_sessionRegistrations.TryGetValue(sessionId, out var pending))
            {
                _sessionRegistrations.Remove(sessionId);
                try
                {
                    pending.TrySetResult(false);
                }
                catch
                {
                }
            }
        }
    }

    public async Task<bool> WaitForSessionRegisteredAsync(string? sessionId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // ADR-010 closure: sessionless is represented by null only.
        // Empty/whitespace session ids are invalid and must not bypass gating.
        if (sessionId is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        TaskCompletionSource<bool>? tcs;
        lock (_sessionSync)
        {
            if (_activeSessions.Contains(sessionId))
            {
                return true;
            }

            if (!_sessionRegistrations.TryGetValue(sessionId, out tcs))
            {
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _sessionRegistrations[sessionId] = tcs;
            }
        }

        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMilliseconds(1);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task && tcs.Task.IsCompletedSuccessfully && tcs.Task.Result;
        }
        catch
        {
            return false;
        }
    }

    public void SetLoaded(
        Process process,
        PluginHostClient client,
        PluginHostEventClient eventClient,
        string pipeName,
        string eventPipeName,
        string hostToken,
        IReadOnlyList<PluginCapabilityDescriptor> capabilities,
        string? capabilitiesError)
    {
        Process = process;
        Client = client;
        EventClient = eventClient;
        PipeName = pipeName;
        EventPipeName = eventPipeName;
        HostToken = hostToken;
        State = PluginLoadState.Loaded;
        Error = null;
        Capabilities = capabilities;
        CapabilitiesError = capabilitiesError;
    }

    public void TryMarkHostRegistered(string token, int processId)
    {
        if (string.IsNullOrWhiteSpace(HostToken))
        {
            return;
        }

        if (!string.Equals(HostToken, token, StringComparison.Ordinal))
        {
            return;
        }

        HostProcessId = processId;
        IsHostRegistered = true;
    }

    public void TryMarkSessionRegistered(string token, int processId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(HostToken))
        {
            return;
        }

        if (!string.Equals(HostToken, token, StringComparison.Ordinal))
        {
            return;
        }

        if (HostProcessId is { } expectedPid && expectedPid != processId)
        {
            return;
        }

        TaskCompletionSource<bool>? tcs;
        lock (_sessionSync)
        {
            // Multi-session: only accept session-registered events for sessions we have initiated.
            if (!_openSessions.Contains(sessionId))
            {
                return;
            }

            // Only accept if we have a pending registration for this session (handshake is connect-scoped).
            if (_activeSessions.Contains(sessionId))
            {
                return;
            }

            if (!_sessionRegistrations.TryGetValue(sessionId, out tcs))
            {
                return;
            }

            _sessionRegistrations.Remove(sessionId);
            _activeSessions.Add(sessionId);
        }

        try
        {
            tcs?.TrySetResult(true);
        }
        catch
        {
        }
    }

    public void SetFailed(string? error)
    {
        DisposeHost();
        State = PluginLoadState.Failed;
        Error = error;
        Capabilities = Array.Empty<PluginCapabilityDescriptor>();
        CapabilitiesError = null;
        HostToken = null;
        IsHostRegistered = false;
        HostProcessId = null;

        lock (_sessionSync)
        {
            FailAllPendingSessionRegistrationsLocked();
            _openSessions.Clear();
            _activeSessions.Clear();
        }
    }

    public void DisposeHost()
    {
        try
        {
            Client?.Dispose();
        }
        catch
        {
        }

        Client = null;

        try
        {
            EventClient?.Dispose();
        }
        catch
        {
        }

        EventClient = null;

        try
        {
            if (Process is { HasExited: false })
            {
                Process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        Process = null;
        PipeName = null;
        EventPipeName = null;
        HostToken = null;
        IsHostRegistered = false;
        HostProcessId = null;
        Capabilities = Array.Empty<PluginCapabilityDescriptor>();
        CapabilitiesError = null;

        lock (_sessionSync)
        {
            FailAllPendingSessionRegistrationsLocked();
            _openSessions.Clear();
            _activeSessions.Clear();
            DisposeAllSessionResourcesLocked();
        }

        lock (_sessionSync)
        {
            foreach (var tcs in _sessionRegistrations.Values)
            {
                try
                {
                    tcs.TrySetCanceled();
                }
                catch
                {
                }
            }

            _sessionRegistrations.Clear();
            _openSessions.Clear();
            _activeSessions.Clear();
            DisposeAllSessionResourcesLocked();
        }
    }
}

public enum PluginLoadState
{
    Loaded,
    Disabled,
    Failed
}
