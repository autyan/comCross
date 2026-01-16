using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private readonly PluginManagerViewModel _pluginManager;
    private readonly LocalizedStringsViewModel _localizedStrings;
    private readonly List<LocaleCultureInfo> _availableLanguages;
    private readonly List<string> _logFormats = new() { "txt", "json" };
    private readonly List<string> _logLevels = new() { "Trace", "Debug", "Info", "Warn", "Error", "Fatal" };
    private LocaleCultureInfo _selectedLanguage;
    private bool _followSystemLanguage;
    private IReadOnlyList<SettingOption<ExportRangeMode>> _exportRangeModeOptions = Array.Empty<SettingOption<ExportRangeMode>>();
    private IReadOnlyList<SettingOption<ConnectionBehavior>> _connectionBehaviorOptions = Array.Empty<SettingOption<ConnectionBehavior>>();
    private bool _saveScheduled;

    public SettingsViewModel(
        SettingsService settingsService,
        ILocalizationService localization,
        LocalizedStringsViewModel localizedStrings,
        PluginManagerViewModel pluginManager)
    {
        _settingsService = settingsService;
        _localization = localization;
        _localizedStrings = localizedStrings;
        _pluginManager = pluginManager;
        _availableLanguages = localization.AvailableCultures.ToList();
        _selectedLanguage = ResolveLanguage(settingsService.Current.Language);
        _followSystemLanguage = settingsService.Current.FollowSystemLanguage;
        RefreshExportOptions();
        RefreshConnectionBehaviorOptions();
    }

    public LocalizedStringsViewModel LocalizedStrings => _localizedStrings;
    public PluginManagerViewModel PluginManager => _pluginManager;
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public IReadOnlyList<LocaleCultureInfo> AvailableLanguages => _availableLanguages;
    public IReadOnlyList<string> AppLogFormats => _logFormats;
    public IReadOnlyList<string> AppLogLevels => _logLevels;

    public LocaleCultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value)
            {
                return;
            }

            // Enforce follow system language priority
            if (FollowSystemLanguage)
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

    public bool AppLogEnabled
    {
        get => _settingsService.Current.AppLogs.Enabled;
        set
        {
            if (_settingsService.Current.AppLogs.Enabled == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Enabled = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogDirectory
    {
        get => _settingsService.Current.AppLogs.Directory;
        set
        {
            if (_settingsService.Current.AppLogs.Directory == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Directory = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogFormat
    {
        get => _settingsService.Current.AppLogs.Format;
        set
        {
            if (_settingsService.Current.AppLogs.Format == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.Format = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string AppLogMinLevel
    {
        get => _settingsService.Current.AppLogs.MinLevel;
        set
        {
            if (_settingsService.Current.AppLogs.MinLevel == value)
            {
                return;
            }

            _settingsService.Current.AppLogs.MinLevel = value;
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

    public bool LogDatabasePersistenceEnabled
    {
        get => _settingsService.Current.Logs.DatabasePersistenceEnabled;
        set
        {
            if (_settingsService.Current.Logs.DatabasePersistenceEnabled == value)
            {
                return;
            }

            _settingsService.Current.Logs.DatabasePersistenceEnabled = value;
            ScheduleSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDatabaseDirectoryEnabled));
        }
    }

    public string? LogDatabaseDirectory
    {
        get => _settingsService.Current.Logs.DatabaseDirectory;
        set
        {
            if (_settingsService.Current.Logs.DatabaseDirectory == value)
            {
                return;
            }

            _settingsService.Current.Logs.DatabaseDirectory = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool IsDatabaseDirectoryEnabled => LogDatabasePersistenceEnabled;

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

    public IReadOnlyList<SettingOption<ConnectionBehavior>> ConnectionBehaviorOptions => _connectionBehaviorOptions;

    public SettingOption<ConnectionBehavior>? SelectedConnectionBehavior
    {
        get => _connectionBehaviorOptions.FirstOrDefault(o => o.Value == _settingsService.Current.Connection.ExistingSessionBehavior);
        set
        {
            if (value == null || _settingsService.Current.Connection.ExistingSessionBehavior == value.Value)
            {
                return;
            }

            _settingsService.Current.Connection.ExistingSessionBehavior = value.Value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public string LinuxScanPatterns
    {
        get => string.Join(Environment.NewLine, _settingsService.Current.Connection.LinuxSerialScan.ScanPatterns);
        set
        {
            var patterns = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _settingsService.Current.Connection.LinuxSerialScan.ScanPatterns = patterns;
            ScheduleSaveAndNotifyLinuxScanChanged();
            OnPropertyChanged();
        }
    }

    public string LinuxExcludePatterns
    {
        get => string.Join(Environment.NewLine, _settingsService.Current.Connection.LinuxSerialScan.ExcludePatterns);
        set
        {
            var patterns = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _settingsService.Current.Connection.LinuxSerialScan.ExcludePatterns = patterns;
            ScheduleSaveAndNotifyLinuxScanChanged();
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

    public string DisplayFontFamily
    {
        get => _settingsService.Current.Display.FontFamily;
        set
        {
            if (_settingsService.Current.Display.FontFamily == value)
            {
                return;
            }

            _settingsService.Current.Display.FontFamily = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int DisplayFontSize
    {
        get => _settingsService.Current.Display.FontSize;
        set
        {
            if (_settingsService.Current.Display.FontSize == value)
            {
                return;
            }

            _settingsService.Current.Display.FontSize = value;
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

    public IReadOnlyList<SettingOption<ExportRangeMode>> ExportRangeModeOptions => _exportRangeModeOptions;

    public SettingOption<ExportRangeMode>? SelectedExportRangeMode
    {
        get => _exportRangeModeOptions.FirstOrDefault(o => o.Value == _settingsService.Current.Export.RangeMode);
        set
        {
            if (value == null || _settingsService.Current.Export.RangeMode == value.Value)
            {
                return;
            }

            _settingsService.Current.Export.RangeMode = value.Value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public int ExportRangeCount
    {
        get => _settingsService.Current.Export.RangeCount;
        set
        {
            if (_settingsService.Current.Export.RangeCount == value)
            {
                return;
            }

            _settingsService.Current.Export.RangeCount = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    public event EventHandler<string>? LanguageChanged;
    public event EventHandler? LinuxScanSettingsChanged;

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
        RefreshExportOptions();
        RefreshConnectionBehaviorOptions();
        LanguageChanged?.Invoke(this, cultureCode);
    }

    private void RefreshExportOptions()
    {
        _exportRangeModeOptions = new[]
        {
            new SettingOption<ExportRangeMode>(ExportRangeMode.All, _localizedStrings.SettingsExportRangeAll),
            new SettingOption<ExportRangeMode>(ExportRangeMode.Latest, _localizedStrings.SettingsExportRangeLatest)
        };

        OnPropertyChanged(nameof(ExportRangeModeOptions));
        OnPropertyChanged(nameof(SelectedExportRangeMode));
    }

    private void RefreshConnectionBehaviorOptions()
    {
        _connectionBehaviorOptions = new[]
        {
            new SettingOption<ConnectionBehavior>(ConnectionBehavior.CreateNew, _localizedStrings.SettingsConnectionBehaviorCreateNew),
            new SettingOption<ConnectionBehavior>(ConnectionBehavior.SwitchToExisting, _localizedStrings.SettingsConnectionBehaviorSwitchToExisting),
            new SettingOption<ConnectionBehavior>(ConnectionBehavior.PromptUser, _localizedStrings.SettingsConnectionBehaviorPromptUser)
        };

        OnPropertyChanged(nameof(ConnectionBehaviorOptions));
        OnPropertyChanged(nameof(SelectedConnectionBehavior));
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
    
    private void ScheduleSaveAndNotifyLinuxScanChanged()
    {
        ScheduleSave();
        LinuxScanSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SettingOption<T>(T Value, string Label);
