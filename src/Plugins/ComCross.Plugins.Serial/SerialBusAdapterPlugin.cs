using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComCross.PluginSdk;

namespace ComCross.Plugins.Serial;

public sealed class SerialBusAdapterPlugin : IConnectableBusAdapterPlugin, IMultiSessionDevicePlugin, IPluginUiStateProvider, IPluginUiStateEventSource
{
    private readonly CancellationTokenSource _cts = new();

    private readonly object _sessionLock = new();
    private readonly HashSet<string> _connectedSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISharedMemoryWriter> _writersBySession = new(StringComparer.Ordinal);

    private const string SerialParametersSchemaResource = "ComCross.Plugins.Serial.Resources.Schemas.serial.parameters.schema.json";
    private const string SerialConnectUiSchemaResource = "ComCross.Plugins.Serial.Resources.Schemas.serial.connect.ui.schema.json";

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
                JsonSchema = ReadEmbeddedResource(SerialParametersSchemaResource),
                UiSchema = ReadEmbeddedResource(SerialConnectUiSchemaResource),
                DefaultParametersJson = "{}",
                SupportsMultiSession = true,
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

        var isConnected = _connected;
        if (!string.IsNullOrWhiteSpace(query.SessionId))
        {
            lock (_sessionLock)
            {
                isConnected = _connectedSessions.Contains(query.SessionId);
            }
        }

        var state = new
        {
            connected = isConnected,
            ports,
            defaultParameters = new
            {
                port = defaultPort,
                baudRate = 115200,
                dataBits = 8,
                parity = "None",
                stopBits = "One",
                flowControl = "None"
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

        if (!TryReadInt(command.Parameters, "baudRate", out var baudRate))
        {
            baudRate = 115200;
        }

        if (baudRate < 1 || baudRate > 10_000_000)
        {
            return Task.FromResult(new PluginConnectResult(false, "Invalid baudRate (expected 1..10000000)."));
        }

        if (!TryReadInt(command.Parameters, "dataBits", out var dataBits))
        {
            dataBits = 8;
        }

        if (dataBits is < 5 or > 8)
        {
            return Task.FromResult(new PluginConnectResult(false, "Invalid dataBits (expected 5..8)."));
        }

        var parityText = TryReadString(command.Parameters, "parity") ?? "None";
        if (!TryParseParity(parityText, out var parity))
        {
            return Task.FromResult(new PluginConnectResult(false, "Invalid parity (expected: None/Odd/Even/Mark/Space)."));
        }

        var stopBitsText = TryReadString(command.Parameters, "stopBits") ?? "One";
        if (!TryParseStopBits(stopBitsText, out var stopBits))
        {
            return Task.FromResult(new PluginConnectResult(false, "Invalid stopBits (expected: None/One/Two/OnePointFive)."));
        }

        var flowControlText = TryReadString(command.Parameters, "flowControl") ?? "None";
        if (!TryParseHandshake(flowControlText, out var handshake))
        {
            return Task.FromResult(new PluginConnectResult(false, "Invalid flowControl (expected: None/XOnXOff/RequestToSend/RequestToSendXOnXOff)."));
        }

        // Optional: allow connect dialog to pass an explicit session name.
        // (The host may also manage this separately.)
        var sessionName = TryReadString(command.Parameters, "sessionName");

        // Best-effort connection test only (current host pipeline does not stream bytes from plugin yet).
        // We just validate that the port can be opened.
        try
        {
            using var serial = new SerialPort(port)
            {
                BaudRate = baudRate,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                Handshake = handshake
            };

            serial.Open();
            serial.Close();

            lock (_sessionLock)
            {
                _connectedSessions.Add(command.SessionId);
                _connected = _connectedSessions.Count > 0;
            }

            return Task.FromResult(new PluginConnectResult(true, SessionId: command.SessionId));
        }
        catch (Exception ex)
        {
            lock (_sessionLock)
            {
                _connectedSessions.Remove(command.SessionId);
                _connected = _connectedSessions.Count > 0;
            }
            return Task.FromResult(new PluginConnectResult(false, ex.Message));
        }
    }

    public Task<PluginCommandResult> DisconnectAsync(PluginDisconnectCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.SessionId))
        {
            lock (_sessionLock)
            {
                _connectedSessions.Remove(command.SessionId);
                _writersBySession.Remove(command.SessionId);
                _connected = _connectedSessions.Count > 0;
            }
        }
        return Task.FromResult(new PluginCommandResult(true));
    }

    public void SetSharedMemoryWriter(ISharedMemoryWriter writer)
    {
        // Not used yet for serial adapter (byte streaming to shared memory will be introduced later).
    }

    public void SetSharedMemoryWriter(string sessionId, ISharedMemoryWriter writer)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionLock)
        {
            _writersBySession[sessionId] = writer;
        }
    }

    public void ClearSharedMemoryWriter(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionLock)
        {
            _writersBySession.Remove(sessionId);
        }
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
                    UiStateInvalidated?.Invoke(this, new PluginUiStateInvalidatedEvent("serial", SessionId: null, ViewKind: "connect-dialog", Reason: "ports-changed"));
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

    private static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(SerialBusAdapterPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Missing embedded resource: {resourceName}. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool TryReadInt(JsonElement obj, string propertyName, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static string? TryReadString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static bool TryParseParity(string value, out Parity parity)
    {
        return value switch
        {
            "None" => (parity = Parity.None) == Parity.None,
            "Odd" => (parity = Parity.Odd) == Parity.Odd,
            "Even" => (parity = Parity.Even) == Parity.Even,
            "Mark" => (parity = Parity.Mark) == Parity.Mark,
            "Space" => (parity = Parity.Space) == Parity.Space,
            _ => Enum.TryParse(value, ignoreCase: true, out parity)
        };
    }

    private static bool TryParseStopBits(string value, out StopBits stopBits)
    {
        return value switch
        {
            "None" => (stopBits = StopBits.None) == StopBits.None,
            "One" => (stopBits = StopBits.One) == StopBits.One,
            "Two" => (stopBits = StopBits.Two) == StopBits.Two,
            "OnePointFive" => (stopBits = StopBits.OnePointFive) == StopBits.OnePointFive,
            _ => Enum.TryParse(value, ignoreCase: true, out stopBits)
        };
    }

    private static bool TryParseHandshake(string value, out Handshake handshake)
    {
        return value switch
        {
            "None" => (handshake = Handshake.None) == Handshake.None,
            "XOnXOff" => (handshake = Handshake.XOnXOff) == Handshake.XOnXOff,
            "RequestToSend" => (handshake = Handshake.RequestToSend) == Handshake.RequestToSend,
            "RequestToSendXOnXOff" => (handshake = Handshake.RequestToSendXOnXOff) == Handshake.RequestToSendXOnXOff,
            _ => Enum.TryParse(value, ignoreCase: true, out handshake)
        };
    }
}
