using System.Diagnostics;
using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

internal sealed record HostStartResult(
    bool Success,
    string? Error,
    Process? Process,
    PluginHostClient? Client,
    PluginHostEventClient? EventClient,
    int? HostProcessId);

internal static class HostSupervisorSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<HostStartResult> StartAsync(
        ProcessStartInfo startInfo,
        string pipeName,
        string eventPipeName,
        string hostToken,
        TimeSpan connectTimeout,
        Action<PluginHostEvent>? onEvent = null,
        Action<Process>? onExited = null,
        CancellationToken cancellationToken = default)
    {
        Process? process = null;
        PluginHostClient? client = null;
        PluginHostEventClient? eventClient = null;

        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new HostStartResult(false, "Failed to start host process.", null, null, null, null);
            }

            if (onExited is not null)
            {
                try
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (_, _) => onExited(process);
                }
                catch
                {
                }
            }

            var registration = new HostRegistrationTracker(hostToken);
            client = new PluginHostClient(pipeName);
            eventClient = new PluginHostEventClient(eventPipeName);
            eventClient.EventReceived += evt =>
            {
                registration.TryObserve(evt);

                try
                {
                    onEvent?.Invoke(evt);
                }
                catch
                {
                }
            };
            eventClient.Start();

            var ping = await client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Ping),
                connectTimeout);

            if (ping is not { Ok: true })
            {
                Cleanup(process, client, eventClient);
                return new HostStartResult(
                    false,
                    ping?.Error ?? "Host failed to respond.",
                    null,
                    null,
                    null,
                    null);
            }

            var registered = await registration.WaitAsync(connectTimeout, cancellationToken);
            if (!registered)
            {
                Cleanup(process, client, eventClient);
                return new HostStartResult(
                    false,
                    "Host registration handshake timed out.",
                    null,
                    null,
                    null,
                    null);
            }

            return new HostStartResult(
                true,
                null,
                process,
                client,
                eventClient,
                registration.ProcessId);
        }
        catch (Exception ex)
        {
            Cleanup(process, client, eventClient);
            return new HostStartResult(false, ex.Message, null, null, null, null);
        }
    }

    private static void Cleanup(
        Process? process,
        PluginHostClient? client,
        PluginHostEventClient? eventClient)
    {
        try
        {
            client?.Dispose();
        }
        catch
        {
        }

        try
        {
            eventClient?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed class HostRegistrationTracker
    {
        private readonly string _hostToken;
        private readonly TaskCompletionSource<int?> _registered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HostRegistrationTracker(string hostToken)
        {
            _hostToken = hostToken;
        }

        public int? ProcessId => _registered.Task.IsCompletedSuccessfully ? _registered.Task.Result : null;

        public void TryObserve(PluginHostEvent evt)
        {
            if (evt.Payload is null
                || !string.Equals(evt.Type, PluginHostEventTypes.HostRegistered, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var payload = evt.Payload.Value.Deserialize<PluginHostRegisteredEvent>(JsonOptions);
                if (payload is null
                    || string.IsNullOrWhiteSpace(payload.Token)
                    || !string.Equals(payload.Token, _hostToken, StringComparison.Ordinal))
                {
                    return;
                }

                _registered.TrySetResult(payload.ProcessId);
            }
            catch
            {
            }
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (_registered.Task.IsCompleted)
            {
                return true;
            }

            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromMilliseconds(1);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                var completed = await Task.WhenAny(_registered.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
                return completed == _registered.Task && _registered.Task.IsCompletedSuccessfully;
            }
            catch
            {
                return false;
            }
        }
    }
}
