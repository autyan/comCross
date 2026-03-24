using ComCross.PluginSdk;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class PluginUiStateFetcher : IPluginUiStateFetcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly PluginHostProtocolService _protocolService;
    private readonly ExtensionRuntimeService _extensionRuntimeService;

    public PluginUiStateFetcher(
        PluginHostProtocolService protocolService,
        ExtensionRuntimeService extensionRuntimeService)
    {
        _protocolService = protocolService ?? throw new ArgumentNullException(nameof(protocolService));
        _extensionRuntimeService = extensionRuntimeService ?? throw new ArgumentNullException(nameof(extensionRuntimeService));
    }

    public Task<(bool Ok, string? Error, PluginUiStateSnapshot? Snapshot)> GetUiStateAsync(
        PluginRuntime runtime,
        PluginHostUiStateInvalidatedEvent invalidated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(invalidated);

        if (runtime.Info.Manifest.PluginType == PluginType.BusAdapter)
        {
            return _protocolService.GetUiStateAsync(
                runtime,
                invalidated.CapabilityId,
                invalidated.SessionId,
                invalidated.ViewKind,
                invalidated.ViewInstanceId,
                Timeout,
                cancellationToken);
        }

        return _extensionRuntimeService.GetUiStateAsync(
            runtime,
            invalidated.PluginId,
            invalidated.CapabilityId,
            invalidated.SessionId,
            invalidated.ViewKind,
            invalidated.ViewInstanceId,
            Timeout,
            cancellationToken);
    }
}
