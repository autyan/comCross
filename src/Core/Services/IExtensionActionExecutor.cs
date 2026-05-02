using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public interface IExtensionActionExecutor
{
    Task ExecuteAsync(
        PluginRuntime runtime,
        PluginHostExtensionActionRequestEvent request,
        CancellationToken cancellationToken = default);
}
