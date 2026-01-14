using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly LocalizedStringsViewModel _localizedStrings;
    private readonly List<LocaleCultureInfo> _availableLanguages;
    private LocaleCultureInfo _selectedLanguage;
    private bool _followSystemLanguage;
    private bool _saveScheduled;

    public SettingsViewModel(
        SettingsService settingsService,
        ILocalizationService localization,
        LocalizedStringsViewModel localizedStrings)
    {
        _settingsService = settingsService;
        _localization = localization;
        _localizedStrings = localizedStrings;
        _availableLanguages = localization.AvailableCultures.ToList();
        _selectedLanguage = ResolveLanguage(settingsService.Current.Language);
        _followSystemLanguage = settingsService.Current.FollowSystemLanguage;
    }

    public LocalizedStringsViewModel LocalizedStrings => _localizedStrings;

    public IReadOnlyList<LocaleCultureInfo> AvailableLanguages => _availableLanguages;

    public LocaleCultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value)
            {
                return;
            }

            _selectedLanguage = value;
            _settingsService.Current.Language = value.Code;
            ApplyLanguage(value.Code);
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool FollowSystemLanguage
    {
        get => _followSystemLanguage;
        set
        {
            if (_followSystemLanguage == value)
            {
                return;
            }

            _followSystemLanguage = value;
            _settingsService.Current.FollowSystemLanguage = value;
            if (value)
            {
                ApplySystemLanguage();
            }

            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool LogAutoSaveEnabled
    {
        get => _settingsService.Current.Logs.AutoSaveEnabled;
        set
        {
            if (_settingsService.Current.Logs.AutoSaveEnabled == value)
            {
                return;
            }

            _settingsService.Current.Logs.AutoSaveEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string LogDirectory
    {
        get => _settingsService.Current.Logs.Directory;
        set
        {
            if (_settingsService.Current.Logs.Directory == value)
            {
                return;
            }

            _settingsService.Current.Logs.Directory = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int LogMaxFileSizeMb
    {
        get => _settingsService.Current.Logs.MaxFileSizeMb;
        set
        {
            if (_settingsService.Current.Logs.MaxFileSizeMb == value)
            {
                return;
            }

            _settingsService.Current.Logs.MaxFileSizeMb = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int LogMaxTotalSizeMb
    {
        get => _settingsService.Current.Logs.MaxTotalSizeMb;
        set
        {
            if (_settingsService.Current.Logs.MaxTotalSizeMb == value)
            {
                return;
            }

            _settingsService.Current.Logs.MaxTotalSizeMb = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool LogAutoDeleteEnabled
    {
        get => _settingsService.Current.Logs.AutoDeleteEnabled;
        set
        {
            if (_settingsService.Current.Logs.AutoDeleteEnabled == value)
            {
                return;
            }

            _settingsService.Current.Logs.AutoDeleteEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsStorageAlertsEnabled
    {
        get => _settingsService.Current.Notifications.StorageAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.StorageAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.StorageAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsConnectionAlertsEnabled
    {
        get => _settingsService.Current.Notifications.ConnectionAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.ConnectionAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.ConnectionAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool NotificationsExportAlertsEnabled
    {
        get => _settingsService.Current.Notifications.ExportAlertsEnabled;
        set
        {
            if (_settingsService.Current.Notifications.ExportAlertsEnabled == value)
            {
                return;
            }

            _settingsService.Current.Notifications.ExportAlertsEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int NotificationsRetentionDays
    {
        get => _settingsService.Current.Notifications.RetentionDays;
        set
        {
            if (_settingsService.Current.Notifications.RetentionDays == value)
            {
                return;
            }

            _settingsService.Current.Notifications.RetentionDays = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int ConnectionDefaultBaudRate
    {
        get => _settingsService.Current.Connection.DefaultBaudRate;
        set
        {
            if (_settingsService.Current.Connection.DefaultBaudRate == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultBaudRate = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string ConnectionDefaultEncoding
    {
        get => _settingsService.Current.Connection.DefaultEncoding;
        set
        {
            if (_settingsService.Current.Connection.DefaultEncoding == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultEncoding = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool ConnectionDefaultAddCr
    {
        get => _settingsService.Current.Connection.DefaultAddCr;
        set
        {
            if (_settingsService.Current.Connection.DefaultAddCr == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultAddCr = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool ConnectionDefaultAddLf
    {
        get => _settingsService.Current.Connection.DefaultAddLf;
        set
        {
            if (_settingsService.Current.Connection.DefaultAddLf == value)
            {
                return;
            }

            _settingsService.Current.Connection.DefaultAddLf = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int DisplayMaxMessages
    {
        get => _settingsService.Current.Display.MaxMessages;
        set
        {
            if (_settingsService.Current.Display.MaxMessages == value)
            {
                return;
            }

            _settingsService.Current.Display.MaxMessages = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool DisplayAutoScroll
    {
        get => _settingsService.Current.Display.AutoScroll;
        set
        {
            if (_settingsService.Current.Display.AutoScroll == value)
            {
                return;
            }

            _settingsService.Current.Display.AutoScroll = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string DisplayTimestampFormat
    {
        get => _settingsService.Current.Display.TimestampFormat;
        set
        {
            if (_settingsService.Current.Display.TimestampFormat == value)
            {
                return;
            }

            _settingsService.Current.Display.TimestampFormat = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string ExportDefaultFormat
    {
        get => _settingsService.Current.Export.DefaultFormat;
        set
        {
            if (_settingsService.Current.Export.DefaultFormat == value)
            {
                return;
            }

            _settingsService.Current.Export.DefaultFormat = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string ExportDefaultDirectory
    {
        get => _settingsService.Current.Export.DefaultDirectory;
        set
        {
            if (_settingsService.Current.Export.DefaultDirectory == value)
            {
                return;
            }

            _settingsService.Current.Export.DefaultDirectory = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public event EventHandler<string>? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReloadFromSettings()
    {
        _followSystemLanguage = _settingsService.Current.FollowSystemLanguage;
        _selectedLanguage = ResolveLanguage(_settingsService.Current.Language);
        OnPropertyChanged(string.Empty);
    }

    public void ApplySystemLanguageIfNeeded()
    {
        if (_settingsService.Current.FollowSystemLanguage)
        {
            ApplySystemLanguage();
        }
        else
        {
            ApplyLanguage(_settingsService.Current.Language);
        }
    }

    private void ApplySystemLanguage()
    {
        var cultureCode = CultureInfo.CurrentUICulture.Name;
        var resolved = ResolveLanguage(cultureCode);
        if (resolved.Code != _selectedLanguage.Code)
        {
            _selectedLanguage = resolved;
            _settingsService.Current.Language = resolved.Code;
            OnPropertyChanged(nameof(SelectedLanguage));
        }

        ApplyLanguage(_selectedLanguage.Code);
    }

    private void ApplyLanguage(string cultureCode)
    {
        _localization.SetCulture(cultureCode);
        _localizedStrings.RefreshStrings();
        LanguageChanged?.Invoke(this, cultureCode);
    }

    private LocaleCultureInfo ResolveLanguage(string cultureCode)
    {
        var match = _availableLanguages.FirstOrDefault(l => l.Code == cultureCode);
        return match ?? _availableLanguages.First();
    }

    private void ScheduleSave()
    {
        if (_saveScheduled)
        {
            return;
        }

        _saveScheduled = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            _saveScheduled = false;
            await _settingsService.SaveAsync();
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
