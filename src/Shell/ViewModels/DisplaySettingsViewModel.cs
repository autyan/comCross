using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// UI-facing projection of display-related settings.
/// Owns reacting to SettingsService.SettingsChanged for display properties.
/// </summary>
public sealed class DisplaySettingsViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;

    public DisplaySettingsViewModel(ILocalizationService localization, SettingsService settingsService)
        : base(localization)
    {
        _settingsService = settingsService;
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public string TimestampFormat => _settingsService.Current.Display.TimestampFormat;
    public bool AutoScrollEnabled => _settingsService.Current.Display.AutoScroll;
    public string MessageFontFamily => _settingsService.Current.Display.FontFamily;
    public int MessageFontSize => _settingsService.Current.Display.FontSize;

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        OnPropertyChanged(nameof(TimestampFormat));
        OnPropertyChanged(nameof(AutoScrollEnabled));
        OnPropertyChanged(nameof(MessageFontFamily));
        OnPropertyChanged(nameof(MessageFontSize));
    }
}
