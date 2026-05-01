using ComCross.Shared.Services;

namespace ComCross.Core.Services;

public sealed class CommandDefaultService
{
    private readonly SettingsService _settingsService;
    private readonly ILocalizationService _localization;

    public CommandDefaultService(
        SettingsService settingsService,
        ILocalizationService localization)
    {
        _settingsService = settingsService;
        _localization = localization;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        var commands = _settingsService.Current.Commands;
        if (commands.DefaultsInitialized)
        {
            return;
        }

        var groupName = _localization.GetString("command.defaults.groupName");
        CommandDefaultCatalog.EnsureDefaults(commands, groupName);
        await _settingsService.SaveAsync(cancellationToken);
    }
}
