using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ComCross.Platform;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class ExtensionRuntimeService
{
    private static readonly TimeSpan HostConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<ExtensionRuntimeService> _logger;
    private readonly PluginSignatureVerificationService _signatureVerification;
    private readonly ConcurrentDictionary<string, PluginRuntime> _activeRuntimes = new(StringComparer.Ordinal);
    private readonly object _hostSync = new();

    private IReadOnlyList<PluginInfo> _hostedPlugins = Array.Empty<PluginInfo>();
    private Process? _hostProcess;
    private PluginHostClient? _hostClient;
    private PluginHostEventClient? _hostEventClient;
    private string? _pipeName;
    private string? _eventPipeName;
    private string? _hostToken;
    private string? _pluginsFilePath;
    private bool _hostRegistered;
    private int? _hostProcessId;

    public ExtensionRuntimeService(
        PluginSignatureVerificationService signatureVerification,
        ILogger<ExtensionRuntimeService> logger)
    {
        _signatureVerification = signatureVerification ?? throw new ArgumentNullException(nameof(signatureVerification));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Action<PluginRuntime, PluginHostEvent>? HostEventReceived;

    public IReadOnlyList<PluginRuntime> LoadPlugins(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlyDictionary<string, bool> enabledMap)
    {
        DisposeHostState();
        _activeRuntimes.Clear();

        var runtimes = new List<PluginRuntime>();
        var loadable = new List<(PluginInfo Plugin, PluginRuntime Runtime)>();

        foreach (var plugin in plugins)
        {
            var enabled = enabledMap.TryGetValue(plugin.Manifest.Id, out var isEnabled) ? isEnabled : true;
            if (!enabled)
            {
                runtimes.Add(PluginRuntime.Disabled(plugin));
                continue;
            }

            var runtime = new PluginRuntime(plugin);
            runtimes.Add(runtime);

            if (!_signatureVerification.IsTrusted(plugin, out var trustError))
            {
                runtime.SetFailed(trustError);
                continue;
            }

            loadable.Add((plugin, runtime));
        }

        if (loadable.Count == 0)
        {
            return runtimes;
        }

        var started = StartHost(loadable.Select(x => x.Plugin).ToList());
        if (!started)
        {
            var error = "Extension host failed to start.";
            foreach (var (_, runtime) in loadable)
            {
                runtime.SetFailed(error);
            }

            return runtimes;
        }

        foreach (var (plugin, runtime) in loadable)
        {
            runtime.SetLoaded(Array.Empty<PluginCapabilityDescriptor>(), null);
            _activeRuntimes[plugin.Manifest.Id] = runtime;
        }

        return runtimes;
    }

    public async Task NotifyAsync(
        PluginNotification notification,
        Action<PluginRuntime, Exception, bool>? onError = null,
        CancellationToken cancellationToken = default)
    {
        notification.ValidateGlobal();

        var runtimes = _activeRuntimes.Values.ToList();
        if (runtimes.Count == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (ok, error, restarted) = await SendAsync(
            () => new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Notify, null, notification),
            retryAfterRestart: true,
            cancellationToken);

        if (ok)
        {
            return;
        }

        PublishNotifyError(runtimes, error ?? new InvalidOperationException("Extension host unavailable."), restarted, onError);
    }

    public async Task PushContextAsync(
        ExtensionContextSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToElement(snapshot, JsonOptions);
        var (ok, error, _) = await SendAsync(
            () => new PluginHostRequest(
                Guid.NewGuid().ToString("N"),
                PluginHostMessageTypes.ExtensionSyncContext,
                Payload: payload),
            retryAfterRestart: true,
            cancellationToken);

        if (!ok && error is not null)
        {
            _logger.LogWarning(error, "Failed to push extension context snapshot.");
        }
    }

    public async Task PushFrameBatchAsync(
        IReadOnlyList<ExtensionFrame> frames,
        CancellationToken cancellationToken = default)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToElement(frames, JsonOptions);
        var (ok, error, _) = await SendAsync(
            () => new PluginHostRequest(
                Guid.NewGuid().ToString("N"),
                PluginHostMessageTypes.ExtensionFramesBatch,
                Payload: payload),
            retryAfterRestart: true,
            cancellationToken);

        if (!ok && error is not null)
        {
            _logger.LogWarning(error, "Failed to push extension frame batch.");
        }
    }

    public async Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        string? pluginId,
        string capabilityId,
        string? sessionId,
        string? viewKind,
        string? viewInstanceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runtime.State != PluginLoadState.Loaded)
        {
            return (false, "Plugin not loaded.", null);
        }

        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return (false, "Missing capabilityId.", null);
        }

        if (sessionId is not null && string.IsNullOrWhiteSpace(sessionId))
        {
            return (false, "Invalid sessionId.", null);
        }

        var payload = JsonSerializer.SerializeToElement(
            new PluginHostGetUiStatePayload(capabilityId, sessionId, viewKind, viewInstanceId, pluginId),
            JsonOptions);

        var (response, error, _) = await SendRequestAsync(
            () => new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.GetUiState, Payload: payload),
            retryAfterRestart: true,
            timeout <= TimeSpan.Zero ? RequestTimeout : timeout,
            cancellationToken);

        if (response is null)
        {
            return (false, error?.Message ?? "Extension host unavailable.", null);
        }

        if (!response.Ok)
        {
            return (false, response.Error, null);
        }

        if (response.Payload is null)
        {
            return (false, "Missing ui-state payload.", null);
        }

        try
        {
            var snapshot = response.Payload.Value.Deserialize<PluginUiStateSnapshot>(JsonOptions);
            return snapshot is null
                ? (false, "Invalid ui-state payload.", null)
                : (true, null, snapshot);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid ui-state payload: {ex.Message}", null);
        }
    }

    public async Task ShutdownAsync(TimeSpan timeout, Action<Exception?>? onError = null)
    {
        var process = _hostProcess;
        if (process is null)
        {
            DisposeHostState();
            _activeRuntimes.Clear();
            return;
        }

        try
        {
            if (_hostClient is not null)
            {
                var requestTimeout = timeout <= TimeSpan.Zero
                    ? TimeSpan.FromMilliseconds(1)
                    : (timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1));

                _ = await _hostClient.SendAsync(
                    new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Shutdown),
                    requestTimeout);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }

        try
        {
            if (timeout > TimeSpan.Zero && !process.HasExited)
            {
                using var cts = new CancellationTokenSource(timeout);
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
        finally
        {
            DisposeHostState();
            _activeRuntimes.Clear();
        }
    }

    private bool RestartHost()
    {
        if (_hostedPlugins.Count == 0)
        {
            return false;
        }

        _logger.LogWarning("Restarting extension host.");

        DisposeHostState();

        var started = StartHost(_hostedPlugins);
        if (!started)
        {
            foreach (var runtime in _activeRuntimes.Values)
            {
                runtime.SetFailed("Extension host restart failed.");
            }
        }
        else
        {
            foreach (var runtime in _activeRuntimes.Values)
            {
                runtime.SetLoaded(Array.Empty<PluginCapabilityDescriptor>(), null);
            }
        }

        return started;
    }

    private bool StartHost(IReadOnlyList<PluginInfo> plugins)
    {
        var hostPath = ResolveHostPath();
        if (hostPath is null)
        {
            _logger.LogError("Extension host executable not found.");
            return false;
        }

        var pipeName = CreatePipeName();
        var eventPipeName = pipeName + "-events";
        var hostToken = Guid.NewGuid().ToString("N");

        if (!TryWritePluginsFile(plugins, out var pluginsFilePath, out var writeError))
        {
            _logger.LogError("Failed to prepare extension host manifest: {Error}", writeError);
            return false;
        }

        var processStart = CreateStartInfo(hostPath, pipeName, eventPipeName, pluginsFilePath, hostToken);
        if (processStart is null)
        {
            TryDeletePluginsFile(pluginsFilePath);
            return false;
        }

        try
        {
            _hostRegistered = false;
            _hostProcessId = null;
            _hostToken = hostToken;

            var process = Process.Start(processStart);
            if (process is null)
            {
                TryDeletePluginsFile(pluginsFilePath);
                return false;
            }

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnHostExited();
            }
            catch
            {
            }

            var client = new PluginHostClient(pipeName);
            var eventClient = new PluginHostEventClient(eventPipeName);
            eventClient.EventReceived += HandleHostEvent;
            eventClient.Start();

            var response = client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Ping),
                HostConnectTimeout).GetAwaiter().GetResult();

            if (response is not { Ok: true })
            {
                _logger.LogWarning("Extension host ping failed: {Error}", response?.Error ?? "unavailable");
                client.Dispose();
                eventClient.Dispose();
                TryTerminate(process);
                TryDeletePluginsFile(pluginsFilePath);
                return false;
            }

            var registeredDeadline = DateTimeOffset.UtcNow + HostConnectTimeout;
            while (!_hostRegistered && DateTimeOffset.UtcNow < registeredDeadline)
            {
                Thread.Sleep(30);
            }

            if (!_hostRegistered)
            {
                client.Dispose();
                eventClient.Dispose();
                TryTerminate(process);
                TryDeletePluginsFile(pluginsFilePath);
                return false;
            }

            lock (_hostSync)
            {
                _hostedPlugins = plugins.ToList();
                _hostProcess = process;
                _hostClient = client;
                _hostEventClient = eventClient;
                _pipeName = pipeName;
                _eventPipeName = eventPipeName;
                _pluginsFilePath = pluginsFilePath;
            }

            _logger.LogInformation("Extension host loaded: PluginCount={Count}, Pid={Pid}", plugins.Count, process.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extension host start failed.");
            TryDeletePluginsFile(pluginsFilePath);
            return false;
        }
    }

    private void HandleHostEvent(PluginHostEvent evt)
    {
        if (evt.Payload is not null
            && string.Equals(evt.Type, PluginHostEventTypes.HostRegistered, StringComparison.Ordinal))
        {
            try
            {
                var payload = evt.Payload.Value.Deserialize<PluginHostRegisteredEvent>(JsonOptions);
                if (payload is not null
                    && !string.IsNullOrWhiteSpace(payload.Token)
                    && string.Equals(payload.Token, _hostToken, StringComparison.Ordinal))
                {
                    _hostRegistered = true;
                    _hostProcessId = payload.ProcessId;
                }
            }
            catch
            {
            }
        }

        var targetRuntimes = ResolveEventTargets(evt);
        foreach (var runtime in targetRuntimes)
        {
            try
            {
                HostEventReceived?.Invoke(runtime, evt);
            }
            catch
            {
            }
        }
    }

    private void OnHostExited()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogWarning("Extension host exited.");
                DisposeHostState();
                foreach (var runtime in _activeRuntimes.Values)
                {
                    runtime.SetFailed("Extension host exited.");
                }
            }
            catch
            {
            }
        });
    }

    private void DisposeHostState()
    {
        lock (_hostSync)
        {
            try
            {
                _hostClient?.Dispose();
            }
            catch
            {
            }

            try
            {
                _hostEventClient?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (_hostProcess is { HasExited: false })
                {
                    _hostProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            TryDeletePluginsFile(_pluginsFilePath);

            _hostClient = null;
            _hostEventClient = null;
            _hostProcess = null;
            _pipeName = null;
            _eventPipeName = null;
            _hostToken = null;
            _pluginsFilePath = null;
            _hostRegistered = false;
            _hostProcessId = null;
            _hostedPlugins = Array.Empty<PluginInfo>();
        }
    }

    private static void PublishNotifyError(
        IReadOnlyList<PluginRuntime> runtimes,
        Exception error,
        bool restarted,
        Action<PluginRuntime, Exception, bool>? onError)
    {
        if (onError is null)
        {
            return;
        }

        foreach (var runtime in runtimes)
        {
            onError(runtime, error, restarted);
        }
    }

    private async Task<(bool Ok, Exception? Error, bool Restarted)> SendAsync(
        Func<PluginHostRequest> requestFactory,
        bool retryAfterRestart,
        CancellationToken cancellationToken)
    {
        var (response, error, restarted) = await SendRequestAsync(
            requestFactory,
            retryAfterRestart,
            RequestTimeout,
            cancellationToken);

        return (response is { Ok: true }, error ?? (response is { Ok: false } ? new InvalidOperationException(response.Error ?? "Extension host unavailable.") : null), restarted);
    }

    private async Task<(PluginHostResponse? Response, Exception? Error, bool Restarted)> SendRequestAsync(
        Func<PluginHostRequest> requestFactory,
        bool retryAfterRestart,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeRuntimes.IsEmpty)
        {
            return (new PluginHostResponse(Guid.NewGuid().ToString("N"), true), null, false);
        }

        if (_hostProcess is null || _hostProcess.HasExited || _hostClient is null)
        {
            var restarted = RestartHost();
            if (!restarted)
            {
                return (null, new InvalidOperationException("Extension host exited."), false);
            }

            if (!retryAfterRestart)
            {
                return (null, new InvalidOperationException("Extension host exited."), true);
            }
        }

        try
        {
            var response = await _hostClient!.SendAsync(requestFactory(), timeout);
            if (response is not null && response.Ok)
            {
                return (response, null, false);
            }

            var error = new InvalidOperationException(response?.Error ?? "Extension host unavailable.");
            if (!retryAfterRestart)
            {
                return (response, error, false);
            }

            var restarted = RestartHost();
            if (!restarted)
            {
                return (response, error, false);
            }

            response = await _hostClient!.SendAsync(requestFactory(), timeout);
            return response is not null && response.Ok
                ? (response, null, true)
                : (response, new InvalidOperationException(response?.Error ?? "Extension host unavailable."), true);
        }
        catch (Exception ex)
        {
            if (!retryAfterRestart)
            {
                return (null, ex, false);
            }

            var restarted = RestartHost();
            if (!restarted)
            {
                return (null, ex, false);
            }

            try
            {
                var response = await _hostClient!.SendAsync(requestFactory(), timeout);
                return response is not null && response.Ok
                    ? (response, null, true)
                    : (response, new InvalidOperationException(response?.Error ?? "Extension host unavailable."), true);
            }
            catch (Exception retryEx)
            {
                return (null, retryEx, true);
            }
        }
    }

    private IReadOnlyList<PluginRuntime> ResolveEventTargets(PluginHostEvent evt)
    {
        var pluginId = TryResolveScopedPluginId(evt);
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return _activeRuntimes.Values.ToList();
        }

        return _activeRuntimes.TryGetValue(pluginId, out var runtime)
            ? new[] { runtime }
            : Array.Empty<PluginRuntime>();
    }

    private string? TryResolveScopedPluginId(PluginHostEvent evt)
    {
        if (evt.Payload is null)
        {
            return null;
        }

        try
        {
            if (string.Equals(evt.Type, PluginHostEventTypes.UiStateInvalidated, StringComparison.Ordinal))
            {
                var invalidated = evt.Payload.Value.Deserialize<PluginHostUiStateInvalidatedEvent>(JsonOptions);
                return invalidated?.PluginId;
            }

            if (string.Equals(evt.Type, PluginHostEventTypes.ExtensionActionRequested, StringComparison.Ordinal))
            {
                var requested = evt.Payload.Value.Deserialize<PluginHostExtensionActionRequestEvent>(JsonOptions);
                return requested?.PluginId;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryWritePluginsFile(
        IReadOnlyList<PluginInfo> plugins,
        out string path,
        out string? error)
    {
        try
        {
            path = Path.Combine(
                Path.GetTempPath(),
                $"comcross-extension-host-{Guid.NewGuid():N}.json");

            var payload = plugins.Select(plugin => new ExtensionHostPluginLoadInfo(
                plugin.Manifest.Id,
                plugin.AssemblyPath,
                plugin.Manifest.EntryPoint)).ToList();

            File.WriteAllText(path, JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            path = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    private static void TryDeletePluginsFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static ProcessStartInfo? CreateStartInfo(
        string hostPath,
        string pipeName,
        string eventPipeName,
        string pluginsFilePath,
        string hostToken)
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
            $"--pipe \"{pipeName}\" --event-pipe \"{eventPipeName}\" --plugins-file \"{pluginsFilePath}\" --host-token \"{hostToken}\" --parent-pid {parentPid}";

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
        var exePath = Path.Combine(baseDir, PlatformInfo.ExtensionHostExecutableName);
        if (File.Exists(exePath))
        {
            return exePath;
        }

        var dllPath = Path.Combine(baseDir, "ComCross.ExtensionHost.dll");
        return File.Exists(dllPath) ? dllPath : null;
    }

    private static string CreatePipeName()
        => $"comcross-extension-{Guid.NewGuid():N}";

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

    private sealed record ExtensionHostPluginLoadInfo(
        string PluginId,
        string PluginPath,
        string EntryPoint);
}
