using System.Diagnostics;
using System.Runtime.InteropServices;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class PluginRuntimeService
{
    private static readonly TimeSpan HostConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

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

    public void NotifyLanguageChanged(
        IReadOnlyList<PluginRuntime> runtimes,
        string cultureCode,
        Action<PluginRuntime, Exception, bool>? onError = null)
    {
        var notification = PluginNotification.LanguageChanged(cultureCode);

        Notify(runtimes, notification, onError);
    }

    public void Notify(
        IReadOnlyList<PluginRuntime> runtimes,
        PluginNotification notification,
        Action<PluginRuntime, Exception, bool>? onError = null)
    {
        foreach (var runtime in runtimes)
        {
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

            var response = runtime.Client?.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Notify, notification),
                RequestTimeout).GetAwaiter().GetResult();

            if (response is { Ok: true })
            {
                continue;
            }

            var error = response?.Error ?? "Plugin host unavailable.";
            var recovered = RestartHost(runtime);
            onError?.Invoke(runtime, new InvalidOperationException(error), recovered);
        }
    }

    private PluginRuntime StartHost(PluginInfo plugin)
    {
        var runtime = new PluginRuntime(plugin);
        return StartHost(runtime) ? runtime : runtime;
    }

    private bool RestartHost(PluginRuntime runtime)
    {
        runtime.DisposeHost();
        return StartHost(runtime);
    }

    private bool StartHost(PluginRuntime runtime)
    {
        var pipeName = CreatePipeName(runtime.Info.Manifest.Id);
        var hostPath = ResolveHostPath();
        if (hostPath is null)
        {
            runtime.SetFailed("Plugin host executable not found.");
            return false;
        }

        var processStart = CreateStartInfo(hostPath, pipeName, runtime.Info);
        if (processStart is null)
        {
            runtime.SetFailed("Unable to create plugin host process.");
            return false;
        }

        try
        {
            var process = Process.Start(processStart);
            if (process is null)
            {
                runtime.SetFailed("Failed to start plugin host.");
                return false;
            }

            var client = new PluginHostClient(pipeName);
            var response = client.SendAsync(
                new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.Ping),
                HostConnectTimeout).GetAwaiter().GetResult();

            if (response is not { Ok: true })
            {
                var error = response?.Error ?? "Plugin host failed to respond.";
                runtime.SetFailed(error);
                client.Dispose();
                TryTerminate(process);
                return false;
            }

            runtime.SetLoaded(process, client, pipeName);
            return true;
        }
        catch (Exception ex)
        {
            runtime.SetFailed(ex.Message);
            return false;
        }
    }

    private static ProcessStartInfo? CreateStartInfo(string hostPath, string pipeName, PluginInfo plugin)
    {
        if (string.IsNullOrWhiteSpace(hostPath))
        {
            return null;
        }

        var args = $"--pipe \"{pipeName}\" --plugin \"{plugin.AssemblyPath}\" --entry \"{plugin.Manifest.EntryPoint}\"";

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
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ComCross.PluginHost.exe"
            : "ComCross.PluginHost";
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
    public string? PipeName { get; private set; }
    public string? Error { get; private set; }

    public static PluginRuntime Disabled(PluginInfo info)
    {
        return new PluginRuntime(info, PluginLoadState.Disabled);
    }

    public void SetLoaded(Process process, PluginHostClient client, string pipeName)
    {
        Process = process;
        Client = client;
        PipeName = pipeName;
        State = PluginLoadState.Loaded;
        Error = null;
    }

    public void SetFailed(string? error)
    {
        DisposeHost();
        State = PluginLoadState.Failed;
        Error = error;
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
    }
}

public enum PluginLoadState
{
    Loaded,
    Disabled,
    Failed
}
