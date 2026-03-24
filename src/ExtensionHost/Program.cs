using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Helpers;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.ExtensionHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        _ = StartParentMonitorIfRequested(args);

        if (!LinuxNoNewPrivs.TryEnable(out _))
        {
        }

        ExtensionCatalog catalog;
        try
        {
            catalog = ExtensionCatalog.Load(options.PluginsFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load extension plugins: {ex.Message}");
            return 3;
        }

        using var eventSink = new ExtensionHostEventSink(options.EventPipeName);
        catalog.BindUiStateEventSink(eventSink.PublishUiStateInvalidated);
        catalog.BindActionRequestSink(eventSink.PublishActionRequested);
        eventSink.PublishHostRegistered(options.HostToken);

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try { shutdownCts.Cancel(); } catch { }
        };

        var server = new ExtensionRpcServer(options.PipeName);
        await server.RunAsync(request => HandleRequest(catalog, request, shutdownCts), shutdownCts.Token);
        return 0;
    }

    private static Task<PluginHostResponse> HandleRequest(
        ExtensionCatalog catalog,
        PluginHostRequest request,
        CancellationTokenSource shutdownCts)
    {
        switch (request.Type)
        {
            case PluginHostMessageTypes.Ping:
                return Task.FromResult(new PluginHostResponse(request.Id, true));
            case PluginHostMessageTypes.Notify:
                return Task.FromResult(HandleNotify(catalog, request));
            case PluginHostMessageTypes.LanguageChanged:
                return Task.FromResult(new PluginHostResponse(request.Id, true));
            case PluginHostMessageTypes.ExtensionSyncContext:
                return Task.FromResult(HandleContextSync(catalog, request));
            case PluginHostMessageTypes.ExtensionFramesBatch:
                return Task.FromResult(HandleFrameBatch(catalog, request));
            case PluginHostMessageTypes.GetUiState:
                return Task.FromResult(HandleGetUiState(catalog, request));
            case PluginHostMessageTypes.Shutdown:
                try { shutdownCts.Cancel(); } catch { }
                return Task.FromResult(new PluginHostResponse(request.Id, true));
            default:
                return Task.FromResult(new PluginHostResponse(
                    request.Id,
                    false,
                    $"Message '{request.Type}' is not supported by extension host."));
        }
    }

    private static PluginHostResponse HandleNotify(ExtensionCatalog catalog, PluginHostRequest request)
    {
        if (request.Notification is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing notification payload.");
        }

        if (!PluginNotificationTypes.IsKnownGlobal(request.Notification.Type))
        {
            return new PluginHostResponse(request.Id, false, $"Unknown notification type: {request.Notification.Type}");
        }

        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Instance is not IPluginNotificationSubscriber subscriber)
            {
                continue;
            }

            try
            {
                subscriber.OnNotification(request.Notification);
            }
            catch (Exception ex)
            {
                return new PluginHostResponse(
                    request.Id,
                    false,
                    $"Extension plugin '{plugin.PluginId}' notification failed: {ex.Message}");
            }
        }

        return new PluginHostResponse(request.Id, true);
    }

    private static PluginHostResponse HandleContextSync(ExtensionCatalog catalog, PluginHostRequest request)
    {
        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing extension context payload.");
        }

        ExtensionContextSnapshot? snapshot;
        try
        {
            snapshot = request.Payload.Value.Deserialize<ExtensionContextSnapshot>(JsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid extension context payload: {ex.Message}");
        }

        if (snapshot is null)
        {
            return new PluginHostResponse(request.Id, false, "Invalid extension context payload.");
        }

        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Instance is not IExtensionContextConsumer consumer)
            {
                continue;
            }

            try
            {
                consumer.OnContextSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                return new PluginHostResponse(
                    request.Id,
                    false,
                    $"Extension plugin '{plugin.PluginId}' context sync failed: {ex.Message}");
            }
        }

        return new PluginHostResponse(request.Id, true);
    }

    private static PluginHostResponse HandleFrameBatch(ExtensionCatalog catalog, PluginHostRequest request)
    {
        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing extension frame batch payload.");
        }

        List<ExtensionFrame>? frames;
        try
        {
            frames = request.Payload.Value.Deserialize<List<ExtensionFrame>>(JsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid extension frame batch payload: {ex.Message}");
        }

        if (frames is null || frames.Count == 0)
        {
            return new PluginHostResponse(request.Id, true);
        }

        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Instance is not IExtensionFrameBatchConsumer consumer)
            {
                continue;
            }

            try
            {
                consumer.OnFrameBatch(frames);
            }
            catch (Exception ex)
            {
                return new PluginHostResponse(
                    request.Id,
                    false,
                    $"Extension plugin '{plugin.PluginId}' frame batch failed: {ex.Message}");
            }
        }

        return new PluginHostResponse(request.Id, true);
    }

    private static PluginHostResponse HandleGetUiState(ExtensionCatalog catalog, PluginHostRequest request)
    {
        if (request.Payload is null)
        {
            return new PluginHostResponse(request.Id, false, "Missing ui-state payload.");
        }

        PluginHostGetUiStatePayload? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<PluginHostGetUiStatePayload>(JsonOptions);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, $"Invalid ui-state payload: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CapabilityId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: missing CapabilityId.");
        }

        if (payload.SessionId is not null && string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return new PluginHostResponse(request.Id, false, "Invalid ui-state payload: invalid SessionId.");
        }

        var targetPlugin = catalog.ResolveUiStateProvider(payload.PluginId, payload.CapabilityId);
        if (targetPlugin is null)
        {
            return new PluginHostResponse(request.Id, false, "Extension plugin does not support ui-state.");
        }

        try
        {
            var snapshot = targetPlugin.Value.Provider.GetUiState(new PluginUiStateQuery(
                payload.CapabilityId,
                payload.SessionId,
                payload.ViewKind,
                payload.ViewInstanceId));

            var resultPayload = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, JsonOptions)).RootElement.Clone();
            return new PluginHostResponse(request.Id, true, Payload: resultPayload);
        }
        catch (Exception ex)
        {
            return new PluginHostResponse(request.Id, false, ex.Message);
        }
    }

    private static bool TryParseOptions(string[] args, out ExtensionHostOptions options, out string error)
    {
        var argsMap = ParseArgs(args);
        if (!argsMap.TryGetValue("--pipe", out var pipeName)
            || !argsMap.TryGetValue("--plugins-file", out var pluginsFilePath))
        {
            options = default!;
            error = "Missing required arguments: --pipe --plugins-file";
            return false;
        }

        argsMap.TryGetValue("--event-pipe", out var eventPipeName);
        argsMap.TryGetValue("--host-token", out var hostToken);

        options = new ExtensionHostOptions(
            pipeName,
            pluginsFilePath,
            string.IsNullOrWhiteSpace(eventPipeName) ? null : eventPipeName,
            string.IsNullOrWhiteSpace(hostToken) ? null : hostToken);
        error = string.Empty;
        return true;
    }

    private static Task? StartParentMonitorIfRequested(string[] args)
    {
        var map = ParseArgs(args);
        if (!map.TryGetValue("--parent-pid", out var pidText) ||
            !int.TryParse(pidText, out var parentPid) ||
            parentPid <= 0)
        {
            return null;
        }

        DateTimeOffset? expectedStartUtc = null;
        if (map.TryGetValue("--parent-start-utc", out var startText) &&
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

    private sealed record ExtensionHostOptions(
        string PipeName,
        string PluginsFilePath,
        string? EventPipeName,
        string? HostToken);

    private sealed record ExtensionPluginLoadInfo(
        string PluginId,
        string PluginPath,
        string EntryPoint);

    private sealed class ExtensionCatalog : IDisposable
    {
        private readonly List<(IPluginUiStateEventSource Source, EventHandler<PluginUiStateInvalidatedEvent> Handler)> _uiStateBindings = new();
        private readonly List<(IExtensionActionRequestSource Source, EventHandler<ExtensionActionRequest> Handler)> _actionBindings = new();

        private ExtensionCatalog(IReadOnlyList<LoadedPlugin> plugins)
        {
            Plugins = plugins;
        }

        public IReadOnlyList<LoadedPlugin> Plugins { get; }

        public static ExtensionCatalog Load(string pluginsFilePath)
        {
            if (!File.Exists(pluginsFilePath))
            {
                throw new FileNotFoundException("Missing plugins file.", pluginsFilePath);
            }

            var descriptors = JsonSerializer.Deserialize<List<ExtensionPluginLoadInfo>>(
                File.ReadAllText(pluginsFilePath, Encoding.UTF8),
                JsonOptions);

            if (descriptors is null || descriptors.Count == 0)
            {
                return new ExtensionCatalog(Array.Empty<LoadedPlugin>());
            }

            var loaded = new List<LoadedPlugin>(descriptors.Count);
            foreach (var descriptor in descriptors)
            {
                var loadContext = new ExtensionPluginLoadContext(descriptor.PluginPath);
                var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(descriptor.PluginPath));
                var type = assembly.GetType(descriptor.EntryPoint, throwOnError: true);
                var instance = Activator.CreateInstance(type!);
                if (instance is null)
                {
                    throw new InvalidOperationException($"Failed to instantiate extension plugin '{descriptor.PluginId}'.");
                }

                loaded.Add(new LoadedPlugin(descriptor.PluginId, instance, loadContext));
            }

            return new ExtensionCatalog(loaded);
        }

        public (string PluginId, IPluginUiStateProvider Provider)? ResolveUiStateProvider(string? pluginId, string capabilityId)
        {
            if (!string.IsNullOrWhiteSpace(pluginId))
            {
                LoadedPlugin? exact = Plugins.FirstOrDefault(plugin =>
                    string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal)
                    && plugin.Instance is IPluginUiStateProvider);

                if (exact is not null && exact.Instance is IPluginUiStateProvider exactProvider)
                {
                    return (exact.PluginId, exactProvider);
                }
            }

            LoadedPlugin? fallback = Plugins.FirstOrDefault(plugin =>
                string.Equals(plugin.PluginId, capabilityId, StringComparison.Ordinal)
                && plugin.Instance is IPluginUiStateProvider);

            if (fallback is not null && fallback.Instance is IPluginUiStateProvider provider)
            {
                return (fallback.PluginId, provider);
            }

            return null;
        }

        public void BindUiStateEventSink(Action<string, PluginUiStateInvalidatedEvent> sink)
        {
            foreach (var plugin in Plugins)
            {
                if (plugin.Instance is not IPluginUiStateEventSource source)
                {
                    continue;
                }

                EventHandler<PluginUiStateInvalidatedEvent> handler = (_, evt) => sink(plugin.PluginId, evt);
                source.UiStateInvalidated += handler;
                _uiStateBindings.Add((source, handler));
            }
        }

        public void BindActionRequestSink(Action<string, ExtensionActionRequest> sink)
        {
            foreach (var plugin in Plugins)
            {
                if (plugin.Instance is not IExtensionActionRequestSource source)
                {
                    continue;
                }

                EventHandler<ExtensionActionRequest> handler = (_, evt) => sink(plugin.PluginId, evt);
                source.ActionRequested += handler;
                _actionBindings.Add((source, handler));
            }
        }

        public void Dispose()
        {
            foreach (var binding in _uiStateBindings)
            {
                try
                {
                    binding.Source.UiStateInvalidated -= binding.Handler;
                }
                catch
                {
                }
            }

            foreach (var binding in _actionBindings)
            {
                try
                {
                    binding.Source.ActionRequested -= binding.Handler;
                }
                catch
                {
                }
            }

            foreach (var plugin in Plugins)
            {
                try
                {
                    if (plugin.Instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    plugin.LoadContext.Unload();
                }
                catch
                {
                }
            }
        }
    }

    private sealed record LoadedPlugin(
        string PluginId,
        object Instance,
        AssemblyLoadContext LoadContext);

    private sealed class ExtensionPluginLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginPath;
        private readonly string? _pluginDir;

        public ExtensionPluginLoadContext(string pluginPath)
            : base($"ComCross-Extension-{Guid.NewGuid():N}", isCollectible: false)
        {
            _pluginPath = Path.GetFullPath(pluginPath);
            _pluginDir = Path.GetDirectoryName(_pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simpleName = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
            if (File.Exists(baseDirCandidate))
            {
                return LoadFromAssemblyPath(baseDirCandidate);
            }

            if (!string.IsNullOrWhiteSpace(_pluginDir))
            {
                var pluginCandidate = Path.Combine(_pluginDir, simpleName + ".dll");
                if (File.Exists(pluginCandidate))
                {
                    return LoadFromAssemblyPath(pluginCandidate);
                }
            }

            return null;
        }
    }

    private sealed class ExtensionRpcServer
    {
        private readonly string _pipeName;

        public ExtensionRpcServer(string pipeName)
        {
            _pipeName = pipeName;
        }

        public async Task RunAsync(
            Func<PluginHostRequest, Task<PluginHostResponse>> handler,
            CancellationToken cancellationToken)
        {
            await using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                PluginHostRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<PluginHostRequest>(line, JsonOptions);
                }
                catch (Exception ex)
                {
                    var invalid = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, ex.Message);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(invalid, JsonOptions));
                    continue;
                }

                if (request is null)
                {
                    var invalid = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, "Invalid request.");
                    await writer.WriteLineAsync(JsonSerializer.Serialize(invalid, JsonOptions));
                    continue;
                }

                var response = await handler(request);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));

                if (request.Type == PluginHostMessageTypes.Shutdown)
                {
                    break;
                }
            }
        }
    }

    private sealed class ExtensionHostEventSink : IDisposable
    {
        private readonly string? _pipeName;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;
        private readonly Queue<string> _pending = new();
        private readonly object _sync = new();

        public ExtensionHostEventSink(string? pipeName)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? null : pipeName;
            if (_pipeName is not null)
            {
                _cts = new CancellationTokenSource();
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            }
        }

        public void PublishHostRegistered(string? token)
        {
            if (_pipeName is null || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var payload = new PluginHostRegisteredEvent(token, Process.GetCurrentProcess().Id);
            var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
            Enqueue(JsonSerializer.Serialize(
                new PluginHostEvent(PluginHostEventTypes.HostRegistered, payloadElement),
                JsonOptions));
        }

        public void PublishUiStateInvalidated(string pluginId, PluginUiStateInvalidatedEvent evt)
        {
            if (_pipeName is null || string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(evt.CapabilityId))
            {
                return;
            }

            if (evt.SessionId is not null && string.IsNullOrWhiteSpace(evt.SessionId))
            {
                return;
            }

            var payload = new PluginHostUiStateInvalidatedEvent(
                evt.CapabilityId,
                evt.SessionId,
                evt.ViewKind,
                evt.ViewInstanceId,
                evt.Reason,
                pluginId);

            Enqueue(JsonSerializer.Serialize(
                new PluginHostEvent(
                    PluginHostEventTypes.UiStateInvalidated,
                    JsonSerializer.SerializeToElement(payload, JsonOptions)),
                JsonOptions));
        }

        public void PublishActionRequested(string pluginId, ExtensionActionRequest request)
        {
            if (_pipeName is null || string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(request.Action))
            {
                return;
            }

            if (request.SessionId is not null && string.IsNullOrWhiteSpace(request.SessionId))
            {
                return;
            }

            JsonElement? payloadElement = null;
            if (request.Payload is not null)
            {
                payloadElement = JsonSerializer.SerializeToElement(request.Payload, JsonOptions);
            }

            var payload = new PluginHostExtensionActionRequestEvent(
                pluginId,
                request.Action,
                request.SessionId,
                payloadElement);

            Enqueue(JsonSerializer.Serialize(
                new PluginHostEvent(
                    PluginHostEventTypes.ExtensionActionRequested,
                    JsonSerializer.SerializeToElement(payload, JsonOptions)),
                JsonOptions));
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            _acceptLoop = null;
        }

        private void Enqueue(string line)
        {
            lock (_sync)
            {
                _pending.Enqueue(line);
                Monitor.PulseAll(_sync);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName!,
                        PipeDirection.Out,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    await using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        string? line = null;
                        lock (_sync)
                        {
                            while (_pending.Count == 0 && !cancellationToken.IsCancellationRequested)
                            {
                                Monitor.Wait(_sync, TimeSpan.FromMilliseconds(200));
                                if (!server.IsConnected)
                                {
                                    break;
                                }
                            }

                            if (_pending.Count > 0)
                            {
                                line = _pending.Dequeue();
                            }
                        }

                        if (line is null)
                        {
                            continue;
                        }

                        await writer.WriteLineAsync(line);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, cancellationToken);
                }
            }
        }
    }
}
