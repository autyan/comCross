using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ComCross.Shell.Views;

public partial class CreateWorkloadDialog : Window, INotifyPropertyChanged
{
    private string _workloadName = string.Empty;
    private string _workloadDescription = string.Empty;
    private string? _nameError;

    public CreateWorkloadDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 任务名称
    /// </summary>
    public string WorkloadName
    {
        get => _workloadName;
        set
        {
            if (_workloadName != value)
            {
                _workloadName = value;
                OnPropertyChanged();
                ValidateName();
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    /// <summary>
    /// 任务描述
    /// </summary>
    public string WorkloadDescription
    {
        get => _workloadDescription;
        set
        {
            if (_workloadDescription != value)
            {
                _workloadDescription = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DescriptionLength));
            }
        }
    }

    /// <summary>
    /// 名称错误提示
    /// </summary>
    public string? NameError
    {
        get => _nameError;
        private set
        {
            if (_nameError != value)
            {
                _nameError = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 描述长度
    /// </summary>
    public int DescriptionLength => WorkloadDescription?.Length ?? 0;

    /// <summary>
    /// 表单是否有效
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(WorkloadName) && string.IsNullOrEmpty(NameError);

    /// <summary>
    /// 验证名称
    /// </summary>
    private void ValidateName()
    {
        if (string.IsNullOrWhiteSpace(WorkloadName))
        {
            NameError = "任务名称不能为空";
        }
        else if (WorkloadName.Length < 2)
        {
            NameError = "任务名称至少需要2个字符";
        }
        else if (WorkloadName.Length > 50)
        {
            NameError = "任务名称不能超过50个字符";
        }
        else
        {
            NameError = null;
        }
    }

    /// <summary>
    /// 取消按钮点击
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// 创建按钮点击
    /// </summary>
    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (IsValid)
        {
            Close(new CreateWorkloadResult
            {
                Name = WorkloadName.Trim(),
                Description = WorkloadDescription?.Trim()
            });
        }
    }

    #region INotifyPropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

/// <summary>
/// 创建任务对话框结果
/// </summary>
public sealed class CreateWorkloadResult
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
