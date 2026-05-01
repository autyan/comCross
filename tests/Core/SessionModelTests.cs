using ComCross.Shared.Models;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class SessionModelTests
{
    [Fact]
    public void Endpoint_UsesProducedDisplaySubtitleBeforeParameters()
    {
        var session = new Session
        {
            Id = "session-produced-endpoint",
            Name = "tcp #1",
            ParametersJson = """{"remoteHost":"127.0.0.1","remotePort":502}""",
            DisplaySubtitle = "127.0.0.1:41000 -> 127.0.0.1:502"
        };

        Assert.Equal("127.0.0.1:41000 -> 127.0.0.1:502", session.Endpoint);
    }

    [Fact]
    public void Endpoint_DoesNotInferNetworkPrivateParameterPairs()
    {
        var session = new Session
        {
            Id = "session-private-network-params",
            Name = "tcp #1",
            ParametersJson = """{"remoteHost":"127.0.0.1","remotePort":502}"""
        };

        Assert.Equal(string.Empty, session.Endpoint);
    }

    [Fact]
    public void Endpoint_DoesNotInferSerialPortParameter()
    {
        var session = new Session
        {
            Id = "session-private-serial-params",
            Name = "serial #1",
            ParametersJson = """{"port":"/dev/ttyUSB0","baudRate":115200}"""
        };

        Assert.Equal(string.Empty, session.Endpoint);
    }

    [Fact]
    public void Endpoint_DoesNotInferGenericEndpointParameter()
    {
        var session = new Session
        {
            Id = "session-private-endpoint-param",
            Name = "custom #1",
            ParametersJson = """{"endpoint":"owned-by-plugin"}"""
        };

        Assert.Equal(string.Empty, session.Endpoint);
    }
}
