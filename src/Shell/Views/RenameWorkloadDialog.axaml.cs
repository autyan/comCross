using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class RenameWorkloadDialog : Window, INotifyPropertyChanged
{
    private string _workloadName = string.Empty;
    private string _originalName = string.Empty;
    private string? _nameError;

    // 无参构造函数用于Avalonia XAML设计器
    public RenameWorkloadDialog() : this(new LocalizedStringsViewModel(new ComCross.Core.Services.LocalizationService()))
    {
    }

    public RenameWorkloadDialog(LocalizedStringsViewModel localizedStrings)
    {
        LocalizedStrings = localizedStrings ?? throw new ArgumentNullException(nameof(localizedStrings));
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// 构造函数（带当前名称）
    /// </summary>
    public RenameWorkloadDialog(LocalizedStringsViewModel localizedStrings, string currentName) : this(localizedStrings)
    {
        _originalName = currentName;
        WorkloadName = currentName;
    }

    /// <summary>
    /// 本地化字符串
    /// </summary>
    public LocalizedStringsViewModel LocalizedStrings { get; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 自动聚焦并选中文本
        var textBox = this.FindControl<TextBox>("NameTextBox");
        if (textBox != null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
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
    /// 表单是否有效
    /// </summary>
    public bool IsValid => 
        !string.IsNullOrWhiteSpace(WorkloadName) && 
        string.IsNullOrEmpty(NameError) &&
        WorkloadName.Trim() != _originalName.Trim();

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
        else if (WorkloadName.Trim() == _originalName.Trim())
        {
            NameError = "新名称与原名称相同";
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
    /// 重命名按钮点击
    /// </summary>
    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (IsValid)
        {
            Close(WorkloadName.Trim());
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
