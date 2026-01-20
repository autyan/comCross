using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class CommandCenterViewModel : BaseViewModel
{
    private readonly CommandService _commandService;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly Core.Services.IWorkspaceCoordinator _workspaceCoordinator;
    private string? _sessionId;
    private string _sessionName = string.Empty;
    private CommandDefinition? _selectedCommand;
    private string _editorName = string.Empty;
    private string _editorPayload = string.Empty;
    private string _editorGroup = string.Empty;
    private string _editorEncoding = "UTF-8";
    private CommandPayloadType _editorType = CommandPayloadType.Text;
    private bool _editorAppendCr;
    private bool _editorAppendLf;
    private CommandScope _editorScope = CommandScope.Global;
    private string _editorHotkey = string.Empty;
    private int _editorSortOrder;

    public CommandCenterViewModel(
        ILocalizationService localization,
        CommandService commandService,
        SettingsService settingsService,
        NotificationService notificationService,
        Core.Services.IWorkspaceCoordinator workspaceCoordinator)
        : base(localization)
    {
        _commandService = commandService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _workspaceCoordinator = workspaceCoordinator;

        // 构造时自动根据当前 Session 加载数据
        _ = LoadAsync();
    }

    public ObservableCollection<CommandDefinition> Commands { get; } = new();

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (_selectedCommand == value)
            {
                return;
            }

            _selectedCommand = value;
            LoadEditor(value);
            OnPropertyChanged();
        }
    }

    public string EditorName
    {
        get => _editorName;
        set
        {
            if (_editorName == value)
            {
                return;
            }

            _editorName = value;
            OnPropertyChanged();
        }
    }

    public string EditorPayload
    {
        get => _editorPayload;
        set
        {
            if (_editorPayload == value)
            {
                return;
            }

            _editorPayload = value;
            OnPropertyChanged();
        }
    }

    public string EditorGroup
    {
        get => _editorGroup;
        set
        {
            if (_editorGroup == value)
            {
                return;
            }

            _editorGroup = value;
            OnPropertyChanged();
        }
    }

    public string EditorEncoding
    {
        get => _editorEncoding;
        set
        {
            if (_editorEncoding == value)
            {
                return;
            }

            _editorEncoding = value;
            OnPropertyChanged();
        }
    }

    public CommandPayloadType EditorType
    {
        get => _editorType;
        set
        {
            if (_editorType == value)
            {
                return;
            }

            _editorType = value;
            OnPropertyChanged();
        }
    }

    public bool EditorAppendCr
    {
        get => _editorAppendCr;
        set
        {
            if (_editorAppendCr == value)
            {
                return;
            }

            _editorAppendCr = value;
            OnPropertyChanged();
        }
    }

    public bool EditorAppendLf
    {
        get => _editorAppendLf;
        set
        {
            if (_editorAppendLf == value)
            {
                return;
            }

            _editorAppendLf = value;
            OnPropertyChanged();
        }
    }

    public CommandScope EditorScope
    {
        get => _editorScope;
        set
        {
            if (_editorScope == value)
            {
                return;
            }

            _editorScope = value;
            OnPropertyChanged();
        }
    }

    public string EditorHotkey
    {
        get => _editorHotkey;
        set
        {
            if (_editorHotkey == value)
            {
                return;
            }

            _editorHotkey = value;
            OnPropertyChanged();
        }
    }

    public int EditorSortOrder
    {
        get => _editorSortOrder;
        set
        {
            if (_editorSortOrder == value)
            {
                return;
            }

            _editorSortOrder = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<CommandOption<CommandPayloadType>> PayloadTypeOptions =>
        new[]
        {
            new CommandOption<CommandPayloadType>(CommandPayloadType.Text, L["tool.commands.type.text"]),
            new CommandOption<CommandPayloadType>(CommandPayloadType.Hex, L["tool.commands.type.hex"])
        };

    public IReadOnlyList<CommandOption<CommandScope>> ScopeOptions =>
        new[]
        {
            new CommandOption<CommandScope>(CommandScope.Global, L["tool.commands.scope.global"]),
            new CommandOption<CommandScope>(CommandScope.Session, L["tool.commands.scope.session"])
        };

    public CommandOption<CommandPayloadType>? SelectedPayloadType
    {
        get => PayloadTypeOptions.FirstOrDefault(o => o.Value == EditorType);
        set
        {
            if (value == null || EditorType == value.Value)
            {
                return;
            }

            EditorType = value.Value;
            OnPropertyChanged();
        }
    }

    public CommandOption<CommandScope>? SelectedScope
    {
        get => ScopeOptions.FirstOrDefault(o => o.Value == EditorScope);
        set
        {
            if (value == null || EditorScope == value.Value)
            {
                return;
            }

            EditorScope = value.Value;
            OnPropertyChanged();
        }
    }

    public event Func<CommandDefinition, Task>? SendRequested;

    public async Task LoadAsync()
    {
        Commands.Clear();
        var items = _commandService.GetAllCommands(_sessionId);
        foreach (var item in items)
        {
            Commands.Add(item);
        }

        SelectedCommand = Commands.FirstOrDefault();
    }

    public void SetSession(string? sessionId, string? sessionName)
    {
        _sessionId = sessionId;
        _sessionName = sessionName ?? string.Empty;
        _ = LoadAsync();
    }

    public async Task SaveAsync()
    {
        var command = SelectedCommand ?? new CommandDefinition();
        command.Name = EditorName.Trim();
        command.Payload = EditorPayload;
        command.Group = EditorGroup.Trim();
        command.Encoding = string.IsNullOrWhiteSpace(EditorEncoding) ? "UTF-8" : EditorEncoding.Trim();
        command.Type = EditorType;
        command.AppendCr = EditorAppendCr;
        command.AppendLf = EditorAppendLf;
        command.Scope = EditorScope;
        command.SessionId = EditorScope == CommandScope.Session ? _sessionId : null;
        command.SortOrder = EditorSortOrder > 0 ? EditorSortOrder : command.SortOrder == 0 ? Commands.Count + 1 : command.SortOrder;
        command.Hotkey = NormalizeHotkey(EditorHotkey);

        await _commandService.AddOrUpdateAsync(command);
        await LoadAsync();
    }

    public void NewCommand()
    {
        SelectedCommand = null;
        EditorName = string.Empty;
        EditorPayload = string.Empty;
        EditorGroup = string.Empty;
        EditorEncoding = "UTF-8";
        EditorType = CommandPayloadType.Text;
        EditorAppendCr = false;
        EditorAppendLf = false;
        EditorScope = CommandScope.Global;
        EditorHotkey = string.Empty;
        EditorSortOrder = Commands.Count + 1;
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedCommand == null)
        {
            return;
        }

        await _commandService.RemoveAsync(SelectedCommand);
        await LoadAsync();
    }

    public async Task SendSelectedAsync()
    {
        if (SelectedCommand == null || string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        try
        {
            byte[] data;
            if (SelectedCommand.Type == CommandPayloadType.Hex)
            {
                data = Convert.FromHexString(SelectedCommand.Payload.Replace(" ", ""));
            }
            else
            {
                var encoding = Shared.Helpers.EncodingHelper.GetEncoding(SelectedCommand.Encoding);
                data = encoding.GetBytes(SelectedCommand.Payload);
            }

            if (SelectedCommand.AppendCr || SelectedCommand.AppendLf)
            {
                var suffix = (SelectedCommand.AppendCr ? "\r" : "") + (SelectedCommand.AppendLf ? "\n" : "");
                var suffixBytes = System.Text.Encoding.UTF8.GetBytes(suffix);
                data = data.Concat(suffixBytes).ToArray();
            }

            await _workspaceCoordinator.SendDataAsync(_sessionId, data);
        }
        catch (Exception ex)
        {
            await _notificationService.AddAsync(
                NotificationCategory.System,
                NotificationLevel.Error,
                "command.send.failed",
                new object[] { ex.Message });
        }
    }

    public async Task ExportAsync(string? filePath = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(_settingsService.Current.Export.DefaultDirectory, "commands.json")
            : filePath;
        await _commandService.ExportAsync(targetPath);
        await _notificationService.AddAsync(
            NotificationCategory.System,
            NotificationLevel.Info,
            "notification.export.completed",
            new object[] { targetPath });
    }

    public async Task ImportAsync(string? filePath = null)
    {
        var sourcePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(_settingsService.Current.Export.DefaultDirectory, "commands.json")
            : filePath;
        await _commandService.ImportAsync(sourcePath);
        await LoadAsync();
    }

    private void LoadEditor(CommandDefinition? command)
    {
        if (command == null)
        {
            return;
        }

        EditorName = command.Name;
        EditorPayload = command.Payload;
        EditorGroup = command.Group;
        EditorEncoding = command.Encoding;
        EditorType = command.Type;
        EditorAppendCr = command.AppendCr;
        EditorAppendLf = command.AppendLf;
        EditorScope = command.Scope;
        EditorHotkey = command.Hotkey ?? string.Empty;
        EditorSortOrder = command.SortOrder == 0 ? 1 : command.SortOrder;
        OnPropertyChanged(nameof(SelectedPayloadType));
        OnPropertyChanged(nameof(SelectedScope));
    }

    public async Task<bool> TryExecuteHotkeyAsync(string? hotkey)
    {
        var normalized = NormalizeHotkey(hotkey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var match = Commands.FirstOrDefault(command =>
            string.Equals(NormalizeHotkey(command.Hotkey), normalized, StringComparison.OrdinalIgnoreCase));

        if (match == null || SendRequested == null)
        {
            return false;
        }

        await SendRequested.Invoke(match);
        return true;
    }

    private static string NormalizeHotkey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace(" ", string.Empty).Trim();
    }

}

public sealed record CommandOption<T>(T Value, string Label);
