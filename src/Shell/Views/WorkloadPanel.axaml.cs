using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class WorkloadPanel : BaseUserControl
{
    private static readonly DataFormat<string> SessionIdFormat =
        DataFormat.CreateInProcessFormat<string>("comcross.session-id");

    private static readonly DataFormat<string> SessionNameFormat =
        DataFormat.CreateInProcessFormat<string>("comcross.session-name");

    public WorkloadPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 处理 Workload 头部点击（展开/折叠）
    /// </summary>
    private void OnWorkloadHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is WorkloadItemViewModel workload)
        {
            // 切换展开/折叠状态
            workload.IsExpanded = !workload.IsExpanded;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 处理 Session 点击（开始拖拽）
    /// </summary>
    private async void OnSessionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && 
            border.DataContext is SessionItemViewModel session &&
            e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            // 开始拖拽操作
            var dragItem = new DataTransferItem();
            dragItem.Set(SessionIdFormat, session.Id);
            dragItem.Set(SessionNameFormat, session.Name);

            var dragData = new DataTransfer();
            dragData.Add(dragItem);

            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);
        }
    }

    // Week 10: Session拖放功能
    /*
    /// <summary>
    /// 处理 Workload 拖放（Session 移动到此 Workload）
    /// </summary>
    private void OnWorkloadDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("SessionId") && 
            sender is Border border && 
            border.DataContext is WorkloadItemViewModel targetWorkload &&
            DataContext is WorkloadPanelViewModel viewModel)
        {
            var sessionId = e.Data.Get("SessionId") as string;
            if (!string.IsNullOrEmpty(sessionId))
            {
                // 执行移动命令
                viewModel.MoveSessionCommand.Execute((sessionId, targetWorkload.Id));
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// 处理拖放悬停（显示放置提示）
    /// </summary>
    private void OnWorkloadDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("SessionId"))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
    */
}
