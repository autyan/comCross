using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComCross.PluginSdk;

namespace ComCross.Plugins.Serial;

public sealed class SerialBusAdapterPlugin : IConnectableBusAdapterPlugin, IPluginUiStateProvider, IPluginUiStateEventSource
{
    private readonly CancellationTokenSource _cts = new();

    private string[] _lastPorts = Array.Empty<string>();
    private volatile bool _connected;

    public SerialBusAdapterPlugin()
    {
        _ = Task.Run(PollPortsAsync);
    }

    public PluginMetadata Metadata { get; } = new()
    {
        Id = "serial.adapter",
        Name = "Serial Adapter",
        Version = "0.3.2",
        Type = PluginType.BusAdapter,
        Description = "Serial port (RS232/RS485/RS422) bus adapter"
    };

    public event EventHandler<PluginUiStateInvalidatedEvent>? UiStateInvalidated;

    public IReadOnlyList<PluginCapabilityDescriptor> GetCapabilities()
    {
        return new[]
        {
            new PluginCapabilityDescriptor
            {
                Id = "serial",
                Name = "Serial (RS232)",
                Description = "Serial port communication (RS232/RS485/RS422)",
                Icon = "🔌",
                JsonSchema = GetSerialParametersSchemaJson(),
                UiSchema = GetSerialConnectUiSchemaJson(),
                DefaultParametersJson = "{}",
                SharedMemoryRequest = new SharedMemoryRequest
                {
                    MinBytes = 64 * 1024,
                    PreferredBytes = 256 * 1024,
                    MaxBytes = 4 * 1024 * 1024,
                    SupportsWriterSwitch = true,
                    GrowthStepBytes = 256 * 1024
                }
            }
        };
    }

    public PluginUiStateSnapshot GetUiState(PluginUiStateQuery query)
    {
        // Convention-based snapshot for main process:
        // - ports: string[]
        // - defaultParameters: object
        var ports = GetPortsSafe();
        var defaultPort = ports.FirstOrDefault();

        var state = new
        {
            connected = _connected,
            ports,
            defaultParameters = new
            {
                port = defaultPort,
                baudRate = 115200,
                dataBits = 8,
                parity = "None",
                stopBits = "One",
                flowControl = "None",
                encoding = "UTF-8"
            }
        };

        var element = JsonSerializer.SerializeToElement(state);
        return new PluginUiStateSnapshot(element, DateTimeOffset.UtcNow);
    }

    public Task<PluginConnectResult> ConnectAsync(PluginConnectCommand command, CancellationToken cancellationToken)
    {
        if (!string.Equals(command.CapabilityId, "serial", StringComparison.Ordinal))
        {
            return Task.FromResult(new PluginConnectResult(false, $"Unknown capability: {command.CapabilityId}"));
        }

        if (string.IsNullOrWhiteSpace(command.SessionId))
        {
            return Task.FromResult(new PluginConnectResult(false, "Missing SessionId."));
        }

        if (command.Parameters.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(new PluginConnectResult(false, "Parameters must be an object."));
        }

        if (!command.Parameters.TryGetProperty("port", out var portProp) || portProp.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(new PluginConnectResult(false, "Missing required parameter: port"));
        }

        var port = portProp.GetString();
        if (string.IsNullOrWhiteSpace(port))
        {
            return Task.FromResult(new PluginConnectResult(false, "Missing required parameter: port"));
        }

        var baudRate = 115200;
        if (command.Parameters.TryGetProperty("baudRate", out var baudProp) && baudProp.ValueKind == JsonValueKind.Number)
        {
            if (!baudProp.TryGetInt32(out baudRate))
            {
                baudRate = 115200;
            }
        }

        // Best-effort connection test only (current host pipeline does not stream bytes from plugin yet).
        // We just validate that the port can be opened.
        try
        {
            using var serial = new SerialPort(port)
            {
                BaudRate = baudRate
            };

            serial.Open();
            serial.Close();
            _connected = true;

            return Task.FromResult(new PluginConnectResult(true, SessionId: command.SessionId));
        }
        catch (Exception ex)
        {
            _connected = false;
            return Task.FromResult(new PluginConnectResult(false, ex.Message));
        }
    }

    public Task<PluginCommandResult> DisconnectAsync(PluginDisconnectCommand command, CancellationToken cancellationToken)
    {
        _connected = false;
        return Task.FromResult(new PluginCommandResult(true));
    }

    public void SetSharedMemoryWriter(ISharedMemoryWriter writer)
    {
        // Not used yet for serial adapter (byte streaming to shared memory will be introduced later).
    }

    public void SetBackpressureLevel(BackpressureLevel level)
    {
        // Not used yet.
    }

    private async Task PollPortsAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                var ports = GetPortsSafe();
                if (!ports.SequenceEqual(_lastPorts, StringComparer.Ordinal))
                {
                    _lastPorts = ports;
                    UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent("serial", SessionId: null, ViewId: "connect-dialog", Reason: "ports-changed"));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch
        {
            // best-effort; avoid crashing the host process.
        }
    }

    private string[] GetPortsSafe()
    {
        try
        {
            var ports = new HashSet<string>(SerialPort.GetPortNames().Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.Ordinal);

            // On Linux, SerialPort.GetPortNames can be incomplete depending on distro/container.
            // Add a conservative scan of common device patterns.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach (var path in ScanLinuxPorts("/dev/ttyUSB*")) ports.Add(path);
                foreach (var path in ScanLinuxPorts("/dev/ttyACM*")) ports.Add(path);
                foreach (var path in ScanLinuxPorts("/dev/ttyAMA*")) ports.Add(path);
                foreach (var path in ScanLinuxPorts("/dev/ttyS*")) ports.Add(path);
            }

            return ports.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
        catch
        {
            try
            {
                return SerialPort.GetPortNames().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    private static IEnumerable<string> ScanLinuxPorts(string pattern)
    {
        try
        {
            var lastSlash = pattern.LastIndexOf('/');
            if (lastSlash < 0)
            {
                return Array.Empty<string>();
            }

            var directory = pattern.Substring(0, lastSlash);
            var filePattern = pattern.Substring(lastSlash + 1);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(filePattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new System.Text.RegularExpressions.Regex(regexPattern);

            return Directory
                .GetFiles(directory)
                .Where(file => regex.IsMatch(Path.GetFileName(file)))
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GetSerialParametersSchemaJson()
    {
        // schema-lite compatible subset
        return "{\"type\":\"object\",\"properties\":{" +
               "\"port\":{\"type\":\"string\"}," +
               "\"baudRate\":{\"type\":\"integer\"}," +
               "\"dataBits\":{\"type\":\"integer\"}," +
               "\"parity\":{\"type\":\"string\",\"enum\":[\"None\",\"Odd\",\"Even\",\"Mark\",\"Space\"]}," +
               "\"stopBits\":{\"type\":\"string\",\"enum\":[\"None\",\"One\",\"Two\",\"OnePointFive\"]}," +
               "\"flowControl\":{\"type\":\"string\",\"enum\":[\"None\",\"XOnXOff\",\"RequestToSend\",\"RequestToSendXOnXOff\"]}," +
               "\"encoding\":{\"type\":\"string\"}," +
               "\"sessionName\":{\"type\":\"string\"}" +
               "},\"required\":[\"port\"]}";
    }

    private static string GetSerialConnectUiSchemaJson()
    {
        // Host-consumed UI descriptor (draft-agnostic, schema-lite friendly).
        // The host should treat this as plugin-owned UI declaration.
        return "{" +
               "\"titleKey\":\"serial.adapter.connect.title\"," +
               "\"fields\":[" +
               "{\"name\":\"port\",\"control\":\"select\",\"labelKey\":\"serial.adapter.connect.port\",\"optionsStatePath\":\"ports\",\"defaultStatePath\":\"defaultParameters.port\",\"required\":true}," +
               "{\"name\":\"baudRate\",\"control\":\"number\",\"labelKey\":\"serial.adapter.connect.baudRate\",\"defaultStatePath\":\"defaultParameters.baudRate\"}," +
               "{\"name\":\"dataBits\",\"control\":\"number\",\"labelKey\":\"serial.adapter.connect.dataBits\",\"defaultStatePath\":\"defaultParameters.dataBits\"}," +
               "{\"name\":\"parity\",\"control\":\"select\",\"labelKey\":\"serial.adapter.connect.parity\",\"enumFromSchema\":true,\"defaultStatePath\":\"defaultParameters.parity\"}," +
               "{\"name\":\"stopBits\",\"control\":\"select\",\"labelKey\":\"serial.adapter.connect.stopBits\",\"enumFromSchema\":true,\"defaultStatePath\":\"defaultParameters.stopBits\"}," +
               "{\"name\":\"flowControl\",\"control\":\"select\",\"labelKey\":\"serial.adapter.connect.flowControl\",\"enumFromSchema\":true,\"defaultStatePath\":\"defaultParameters.flowControl\"}," +
               "{\"name\":\"encoding\",\"control\":\"text\",\"labelKey\":\"serial.adapter.connect.encoding\",\"defaultStatePath\":\"defaultParameters.encoding\"}," +
               "{\"name\":\"sessionName\",\"control\":\"text\",\"labelKey\":\"serial.adapter.connect.sessionName\"}" +
               "]," +
               "\"actions\":[" +
               "{\"id\":\"connect\",\"labelKey\":\"serial.adapter.connect.action.connect\",\"kind\":\"host\",\"hostAction\":\"comcross.session.connect\",\"extraParameters\":{\"adapter\":\"serial\"}}" +
               "]" +
               "}";
    }
}
