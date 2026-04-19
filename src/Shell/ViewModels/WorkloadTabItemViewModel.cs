using System;
using System.Windows.Input;
using ComCross.Core.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed record WorkloadTabItemContext(
    Workload Workload,
    ICommand ActivateCommand,
    ICommand DeleteCommand,
    ICommand RenameCommand,
    ICommand CopyCommand);

/// <summary>
/// ViewModel for individual Workload tab item
/// </summary>
public sealed class WorkloadTabItemViewModel : BaseViewModel, IInitializable<WorkloadTabItemContext>
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private bool _isActive;
    private string _icon = "📁";
    private bool _isDefault;
    private bool _isInitialized;

    public WorkloadTabItemViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public void Init(WorkloadTabItemContext context)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("WorkloadTabItemViewModel already initialized.");
        }

        _isInitialized = true;

        _id = context.Workload.Id;
        _name = context.Workload.Name;
        _icon = context.Workload.IsDefault ? "🏠" : "📁";
        _isDefault = context.Workload.IsDefault;

        ActivateCommand = context.ActivateCommand;
        DeleteCommand = context.DeleteCommand;
        RenameCommand = context.RenameCommand;
        CopyCommand = context.CopyCommand;

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Workload unique ID
    /// </summary>
    public string Id => _id;

    /// <summary>
    /// Workload name
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(FullName));
            }
        }
    }

    /// <summary>
    /// Display name (max 10 Chinese chars, truncated with "...")
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(_name))
                return string.Empty;

            // 计算字符串宽度（中文字符算2，英文算1）
            int width = 0;
            int charCount = 0;
            foreach (char c in _name)
            {
                width += c > 127 ? 2 : 1; // 简单判断中英文
                charCount++;
                if (width > 20) // 10个中文字符 = 20宽度
                {
                    return _name.Substring(0, charCount - 1) + "...";
                }
            }
            return _name;
        }
    }

    /// <summary>
    /// Full name for tooltip
    /// </summary>
    public string FullName => _name;

    /// <summary>
    /// Workload icon
    /// </summary>
    public string Icon => _icon;

    /// <summary>
    /// Is this the default workload?
    /// </summary>
    public bool IsDefault => _isDefault;

    /// <summary>
    /// Is this workload currently active?
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Can this workload be deleted? (Default workload cannot be deleted)
    /// </summary>
    public bool CanDelete => !IsDefault;

    /// <summary>
    /// Can this workload be renamed? (All workloads can be renamed)
    /// </summary>
    public bool CanRename => true;

    /// <summary>
    /// Can this workload be copied? (All workloads can be copied)
    /// </summary>
    public bool CanCopy => true;

    public string DefaultBadge => L["workload.defaultBadge"];

    public string ActiveBadge => L["workload.activeBadge"];

    public string RenameHeader => L["workload.rename"];

    public string CopyHeader => L["workload.copy"];

    public string DeleteHeader => L["workload.delete"];

    // Commands are assigned during Init.
    public ICommand ActivateCommand { get; private set; } = null!;

    public ICommand DeleteCommand { get; private set; } = null!;

    public ICommand RenameCommand { get; private set; } = null!;

    public ICommand CopyCommand { get; private set; } = null!;
}
