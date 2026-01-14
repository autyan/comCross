using System.Text.Json;
using ComCross.Shared.Models;

namespace ComCross.Core.Services;

public sealed class CommandService
{
    private readonly SettingsService _settingsService;

    public CommandService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<CommandDefinition> GetGlobalCommands()
    {
        return _settingsService.Current.Commands.GlobalCommands
            .OrderBy(c => c.SortOrder)
            .ToList();
    }

    public IReadOnlyList<CommandDefinition> GetSessionCommands(string sessionId)
    {
        if (_settingsService.Current.Commands.SessionCommands.TryGetValue(sessionId, out var list))
        {
            return list.OrderBy(c => c.SortOrder).ToList();
        }

        return Array.Empty<CommandDefinition>();
    }

    public IReadOnlyList<CommandDefinition> GetAllCommands(string? sessionId)
    {
        var combined = new List<CommandDefinition>();
        combined.AddRange(GetGlobalCommands());
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            combined.AddRange(GetSessionCommands(sessionId));
        }

        return combined
            .OrderBy(c => c.Group)
            .ThenBy(c => c.SortOrder)
            .ToList();
    }

    public async Task AddOrUpdateAsync(CommandDefinition command, CancellationToken cancellationToken = default)
    {
        if (command.Scope == CommandScope.Session)
        {
            if (string.IsNullOrWhiteSpace(command.SessionId))
            {
                return;
            }

            var sessionCommands = GetOrCreateSessionList(command.SessionId);
            Upsert(sessionCommands, command);
        }
        else
        {
            Upsert(_settingsService.Current.Commands.GlobalCommands, command);
        }

        await _settingsService.SaveAsync(cancellationToken);
    }

    public async Task RemoveAsync(CommandDefinition command, CancellationToken cancellationToken = default)
    {
        if (command.Scope == CommandScope.Session)
        {
            if (string.IsNullOrWhiteSpace(command.SessionId))
            {
                return;
            }

            if (_settingsService.Current.Commands.SessionCommands.TryGetValue(command.SessionId, out var list))
            {
                list.RemoveAll(c => c.Id == command.Id);
            }
        }
        else
        {
            _settingsService.Current.Commands.GlobalCommands.RemoveAll(c => c.Id == command.Id);
        }

        await _settingsService.SaveAsync(cancellationToken);
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var data = _settingsService.Current.Commands;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var data = JsonSerializer.Deserialize<CommandSettings>(json);
        if (data == null)
        {
            return;
        }

        _settingsService.Current.Commands = data;
        await _settingsService.SaveAsync(cancellationToken);
    }

    private static void Upsert(List<CommandDefinition> list, CommandDefinition command)
    {
        var existing = list.FirstOrDefault(c => c.Id == command.Id);
        if (existing == null)
        {
            list.Add(command);
        }
        else
        {
            var index = list.IndexOf(existing);
            list[index] = command;
        }
    }

    private List<CommandDefinition> GetOrCreateSessionList(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return _settingsService.Current.Commands.GlobalCommands;
        }

        if (!_settingsService.Current.Commands.SessionCommands.TryGetValue(sessionId, out var list))
        {
            list = new List<CommandDefinition>();
            _settingsService.Current.Commands.SessionCommands[sessionId] = list;
        }

        return list;
    }
}
