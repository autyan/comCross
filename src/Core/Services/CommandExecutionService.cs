using ComCross.Shared.Helpers;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class CommandExecutionService
{
    private readonly IWorkspaceCoordinator _workspaceCoordinator;

    public CommandExecutionService(IWorkspaceCoordinator workspaceCoordinator)
    {
        _workspaceCoordinator = workspaceCoordinator ?? throw new ArgumentNullException(nameof(workspaceCoordinator));
    }

    public async Task ExecuteAsync(
        string sessionId,
        CommandDefinition command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("A target session is required.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(command);

        if (command.Type == CommandPayloadType.Hex)
        {
            await _workspaceCoordinator.SendMessageAsync(
                sessionId,
                command.Payload,
                MessageFormat.Hex,
                command.AppendCr,
                command.AppendLf);
            return;
        }

        if (UsesDefaultUtf8(command.Encoding))
        {
            await _workspaceCoordinator.SendMessageAsync(
                sessionId,
                command.Payload,
                MessageFormat.Text,
                command.AppendCr,
                command.AppendLf);
            return;
        }

        var encoding = EncodingHelper.GetEncoding(command.Encoding);
        var data = encoding.GetBytes(command.Payload);
        if (command.AppendCr || command.AppendLf)
        {
            var suffix = (command.AppendCr ? "\r" : "") + (command.AppendLf ? "\n" : "");
            data = data.Concat(encoding.GetBytes(suffix)).ToArray();
        }

        await _workspaceCoordinator.SendDataAsync(sessionId, data);
    }

    private static bool UsesDefaultUtf8(string? encoding)
        => string.IsNullOrWhiteSpace(encoding)
           || string.Equals(encoding, "UTF-8", StringComparison.OrdinalIgnoreCase)
           || string.Equals(encoding, "UTF8", StringComparison.OrdinalIgnoreCase);
}
