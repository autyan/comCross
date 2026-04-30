using System.Diagnostics;
using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shared.Helpers;
using ComCross.PluginHost.Logging;
using ComCross.PluginHost.Events;
using ComCross.PluginHost.Runtime;
using ComCross.PluginHost.Ipc;
using ComCross.PluginHost.Hosting;

namespace ComCross.PluginHost;
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!PluginHostBootstrap.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var fileKey = PluginHostLogKey.Build(Process.GetCurrentProcess().Id, options.PluginId, options.Role);
        var logService = new PluginHostLogService();
        logService.Initialize(new PluginHostLogOptions(
            Directory: string.IsNullOrWhiteSpace(options.LogDir) ? string.Empty : options.LogDir,
            Format: string.IsNullOrWhiteSpace(options.LogFormat) ? "txt" : options.LogFormat,
            MinLevel: string.IsNullOrWhiteSpace(options.LogMinLevel) ? "Info" : options.LogMinLevel,
            FileKey: fileKey,
            ArchiveAboveBytes: 30L * 1024 * 1024,
            RetentionDays: 15));

        // Linux hardening: prevent this host process (and its children) from gaining new privileges via exec.
        if (!LinuxNoNewPrivs.TryEnable(out var nnpError))
        {
            logService.Warn($"no_new_privs could not be enabled: {nnpError}");
        }

        logService.Info($"PluginHost starting: role={options.Role}, pluginId={options.PluginId}, pluginPath={options.PluginPath}, entry={options.EntryPoint}");

        _ = PluginHostBootstrap.StartParentMonitorIfRequested(args);

        var state = new HostRuntime(options.EntryPoint, options.PluginPath, options.FixedSessionId);
        state.SetHostToken(options.HostToken);
        state.TryLoadPlugin();

        using var eventSink = new HostEventSink(options.EventPipeName);
        // Allow both UI host and session host to publish UI-state invalidation.
        // This is required for listener-style session plugins that discover peers in the session host.
        state.SetUiStateEventSink(eventSink.PublishUiStateInvalidated);
        state.SetSessionLifecycleEventSink(eventSink.PublishSessionClosed);

        state.SetSessionRegisteredSink(eventSink.PublishSessionRegistered);
        eventSink.PublishHostRegistered(options.HostToken);

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try { shutdownCts.Cancel(); } catch { }
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var router = new PluginHostRequestRouter(options.Role, state, jsonOptions);
        var rpcServer = new PluginHostRpcServer(options.PipeName, jsonOptions, logService);
        await rpcServer.RunAsync(router.HandleAsync, shutdownCts.Token);

        logService.Info("PluginHost shutting down.");
        return 0;
    }

}
