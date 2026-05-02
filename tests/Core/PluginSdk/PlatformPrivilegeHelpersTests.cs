using System.Net;
using ComCross.PluginSdk.Platform;
using Xunit;

namespace ComCross.Tests.Core.PluginSdk;

public sealed class PlatformPrivilegeHelpersTests
{
    [Fact]
    public void LinuxSerial_GetManualInstructions_IsNonEmptyOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var text = LinuxSerialDevicePermissions.GetManualPermissionInstructions("/dev/ttyUSB0");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("dialout", text);
    }

    [Fact]
    public void LinuxNetworkBind_HasCapNetBindService_DoesNotThrow()
    {
        // Best-effort: should never throw regardless of platform.
        _ = LinuxNetworkBindPermissions.HasCapNetBindService();
    }

    [Fact]
    public void LinuxNetworkBind_CheckCanBind_ReturnsAlreadyGrantedForHighPorts()
    {
        var result = LinuxNetworkBindPermissions.CheckCanBind(IPAddress.Loopback, 1024);
        Assert.Equal(PrivilegeRequestResult.AlreadyGranted, result);
    }
}
