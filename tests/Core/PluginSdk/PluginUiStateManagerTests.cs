using ComCross.PluginSdk.UI;
using Xunit;

namespace ComCross.Tests.Core.PluginSdk;

public sealed class PluginUiStateManagerTests
{
    [Fact]
    public void SetStateSnapshot_SeparatesSessionAndResourceScopedState()
    {
        var manager = new PluginUiStateManager();
        var scope = PluginUiViewScope.From("listener", "panel-1");

        manager.SetStateSnapshot(
            scope,
            sessionId: "listener-1",
            new Dictionary<string, object> { ["connected"] = true });
        manager.SetStateSnapshot(
            scope,
            sessionId: "listener-1",
            new Dictionary<string, object> { ["items"] = 3 },
            resourceKind: "pending-client-list",
            resourceId: "all");

        var sessionState = manager.GetState(scope, "listener-1");
        var resourceState = manager.GetState(scope, "listener-1", "pending-client-list", "all");

        Assert.True(sessionState.ContainsKey("connected"));
        Assert.False(sessionState.ContainsKey("items"));
        Assert.True(resourceState.ContainsKey("items"));
        Assert.False(resourceState.ContainsKey("connected"));
    }
}
