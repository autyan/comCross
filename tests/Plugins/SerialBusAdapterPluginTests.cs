using System.Collections.Generic;
using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Plugins.Serial;
using Xunit;

namespace ComCross.Tests.Plugins;

public sealed class SerialBusAdapterPluginTests
{
    [Fact]
    public async Task StartupInitialization_ProducesSerialSessionFactsAndNormalizedParameters()
    {
        var plugin = new SerialBusAdapterPlugin();
        var parameters = JsonSerializer.Serialize(new
        {
            port = "/dev/ttyUSB0",
            sessionName = "Meter",
        });

        var result = await plugin.InitializeSessionStateAsync(
            NewInitializationContext("serial", parameters),
            CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.StoragePatch?.SchemaVersion);
        Assert.NotNull(result.SessionPatch);
        Assert.Equal("Serial Port", result.SessionPatch!.DisplayTitle);
        Assert.Equal("/dev/ttyUSB0", result.SessionPatch.DisplaySubtitle);
        Assert.Equal("CableIcon", result.SessionPatch.DisplayIcon);
        Assert.True(result.SessionPatch.CanReconnect);
        Assert.NotNull(result.SessionPatch.ParametersJson);

        using var doc = JsonDocument.Parse(result.SessionPatch.ParametersJson!);
        var normalized = doc.RootElement;
        Assert.Equal("/dev/ttyUSB0", normalized.GetProperty("port").GetString());
        Assert.Equal(115200, normalized.GetProperty("baudRate").GetInt32());
        Assert.Equal(8, normalized.GetProperty("dataBits").GetInt32());
        Assert.Equal("None", normalized.GetProperty("parity").GetString());
        Assert.Equal("One", normalized.GetProperty("stopBits").GetString());
        Assert.Equal("None", normalized.GetProperty("flowControl").GetString());
        Assert.Equal("Meter", normalized.GetProperty("sessionName").GetString());
    }

    [Fact]
    public async Task StartupInitialization_IgnoresUnknownCapability()
    {
        var plugin = new SerialBusAdapterPlugin();

        var result = await plugin.InitializeSessionStateAsync(
            NewInitializationContext("other", """{"port":"/dev/ttyUSB0"}"""),
            CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.StoragePatch);
        Assert.Null(result.SessionPatch);
    }

    private static PluginSessionStateInitializationContext NewInitializationContext(
        string capabilityId,
        string? parametersJson)
        => new(
            "serial.adapter",
            capabilityId,
            "serial-session",
            "0.3.2",
            null,
            parametersJson,
            new PluginSessionStorageSnapshot(0, new Dictionary<string, JsonElement>()));
}
