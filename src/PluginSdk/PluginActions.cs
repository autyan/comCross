using System.Text.Json;

namespace ComCross.PluginSdk;

public sealed record PluginActionCommand(
    string ActionName,
    string? SessionId,
    JsonElement Parameters);

public interface IPluginActionHandler
{
    Task<PluginCommandResult> ExecuteActionAsync(PluginActionCommand command, CancellationToken cancellationToken);
}
