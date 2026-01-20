using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class RenameWorkloadDialog : BaseWindow
{
    public RenameWorkloadDialog()
    {
        InitializeComponent();
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
        if (DataContext is not RenameWorkloadDialogViewModel vm || !vm.IsValid)
        {
            return;
        }

        Close(vm.WorkloadName.Trim());
    }
}
