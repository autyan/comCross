using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class CreateWorkloadDialog : BaseWindow
{
    public CreateWorkloadDialog()
    {
        InitializeComponent();
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
        if (DataContext is not CreateWorkloadDialogViewModel vm || !vm.IsValid)
        {
            return;
        }

        Close(new CreateWorkloadResult
        {
            Name = vm.WorkloadName.Trim(),
            Description = vm.WorkloadDescription?.Trim()
        });
    }
}

/// <summary>
/// 创建任务对话框结果
/// </summary>
public sealed class CreateWorkloadResult
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
