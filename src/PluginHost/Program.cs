using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

var argsMap = ParseArgs(args);

if (!argsMap.TryGetValue("--pipe", out var pipeName) ||
    !argsMap.TryGetValue("--plugin", out var pluginPath) ||
    !argsMap.TryGetValue("--entry", out var entryPoint))
{
    Console.Error.WriteLine("Missing required arguments: --pipe --plugin --entry");
    return 2;
}

var state = new HostState(entryPoint, pluginPath);
state.TryLoadPlugin();

var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
await server.WaitForConnectionAsync();

using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
using var writer = new StreamWriter(server, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
{
    AutoFlush = true
};

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null)
    {
        break;
    }

    PluginHostRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<PluginHostRequest>(line, jsonOptions);
    }
    catch (Exception ex)
    {
        var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, ex.Message);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
        continue;
    }

    if (request is null)
    {
        var response = new PluginHostResponse(Guid.NewGuid().ToString("N"), false, "Invalid request.");
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
        continue;
    }

    var responseMessage = request.Type switch
    {
        PluginHostMessageTypes.Ping => HandlePing(request, state),
        PluginHostMessageTypes.Notify => HandleNotify(request, state),
        PluginHostMessageTypes.Shutdown => HandleShutdown(request),
        _ => new PluginHostResponse(request.Id, false, $"Unknown request type: {request.Type}")
    };

    await writer.WriteLineAsync(JsonSerializer.Serialize(responseMessage, jsonOptions));

    if (request.Type == PluginHostMessageTypes.Shutdown)
    {
        break;
    }
}

return 0;

static PluginHostResponse HandlePing(PluginHostRequest request, HostState state)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    return new PluginHostResponse(request.Id, true);
}

static PluginHostResponse HandleNotify(PluginHostRequest request, HostState state)
{
    if (!state.IsLoaded)
    {
        return new PluginHostResponse(request.Id, false, state.LoadError ?? "Plugin load failed.");
    }

    if (request.Notification is null)
    {
        return new PluginHostResponse(request.Id, false, "Missing notification payload.");
    }

    if (state.Instance is not IPluginNotificationSubscriber subscriber)
    {
        return new PluginHostResponse(request.Id, true);
    }

    try
    {
        subscriber.OnNotification(request.Notification);
        return new PluginHostResponse(request.Id, true);
    }
    catch (Exception ex)
    {
        var restarted = state.TryRestart();
        return new PluginHostResponse(request.Id, false, ex.Message, restarted);
    }
}

static PluginHostResponse HandleShutdown(PluginHostRequest request)
{
    return new PluginHostResponse(request.Id, true);
}

static Dictionary<string, string> ParseArgs(string[] args)
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

sealed class HostState
{
    private readonly string _entryPoint;
    private readonly string _pluginPath;

    public HostState(string entryPoint, string pluginPath)
    {
        _entryPoint = entryPoint;
        _pluginPath = pluginPath;
    }

    public object? Instance { get; private set; }
    public string? LoadError { get; private set; }
    public bool IsLoaded => Instance != null && LoadError is null;

    public void TryLoadPlugin()
    {
        try
        {
            var assembly = Assembly.LoadFrom(_pluginPath);
            var type = assembly.GetType(_entryPoint, throwOnError: true);
            Instance = Activator.CreateInstance(type!);
            LoadError = null;
        }
        catch (Exception ex)
        {
            Instance = null;
            LoadError = ex.Message;
        }
    }

    public bool TryRestart()
    {
        TryLoadPlugin();
        return IsLoaded;
    }
}
