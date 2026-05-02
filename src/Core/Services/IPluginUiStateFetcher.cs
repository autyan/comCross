using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public interface IPluginUiStateFetcher
{
    Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        PluginHostUiStateInvalidatedEvent invalidated,
        CancellationToken cancellationToken = default);
}
