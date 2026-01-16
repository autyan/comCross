using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ComCross.Core.Models;
using ComCross.Shared.Models;
using ReactiveUI;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for individual Workload tab item
/// </summary>
public sealed class WorkloadTabItemViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isActive;

    public WorkloadTabItemViewModel(Workload workload, ICommand activateCommand, ICommand closeCommand, ICommand renameCommand, ICommand copyCommand)
    {
        Id = workload.Id;
        _name = workload.Name;
        Icon = workload.IsDefault ? "🏠" : "📁";
        IsDefault = workload.IsDefault;

        ActivateCommand = activateCommand;
        CloseCommand = closeCommand;
        RenameCommand = renameCommand;
        CopyCommand = copyCommand;
    }

    /// <summary>
    /// Workload unique ID
    /// </summary>
    public string Id { get; }

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
    public string Icon { get; }

    /// <summary>
    /// Is this the default workload?
    /// </summary>
    public bool IsDefault { get; }

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
    /// Can this workload be closed? (Default workload cannot be closed)
    /// </summary>
    public bool CanClose => !IsDefault;

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

    /// <summary>
    /// Command to activate this workload
    /// </summary>
    public ICommand ActivateCommand { get; }

    /// <summary>
    /// Command to close this workload
    /// </summary>
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Command to rename this workload
    /// </summary>
    public ICommand RenameCommand { get; }

    /// <summary>
    /// Command to copy this workload (state + data)
    /// </summary>
    public ICommand CopyCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
