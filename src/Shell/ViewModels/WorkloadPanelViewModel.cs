using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// Workload 面板的 ViewModel，管理所有 Workload 的显示和操作
/// </summary>
public sealed class WorkloadPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WorkloadService _workloadService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkloadPanelViewModel> _logger;
    private readonly LocalizedStringsViewModel _localizedStrings;
    private WorkloadItemViewModel? _selectedWorkload;
    private bool _isLoading;
    
    // Event subscriptions
    private readonly List<IDisposable> _subscriptions = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkloadPanelViewModel(
        WorkloadService workloadService,
        IEventBus eventBus,
        ILogger<WorkloadPanelViewModel> logger,
        LocalizedStringsViewModel localizedStrings)
    {
        _workloadService = workloadService;
        _eventBus = eventBus;
        _logger = logger;
        _localizedStrings = localizedStrings;

        Workloads = new ObservableCollection<WorkloadItemViewModel>();

        // 初始化命令
        CreateWorkloadCommand = new AsyncRelayCommand(CreateWorkloadAsync);
        RenameWorkloadCommand = new AsyncRelayCommand(RenameWorkloadAsync);
        DeleteWorkloadCommand = new AsyncRelayCommand(DeleteWorkloadAsync);
        // MoveSessionCommand暂时不实现，Week 9不支持Session拖放

        // 订阅事件
        _subscriptions.Add(_eventBus.Subscribe<WorkloadCreatedEvent>(OnWorkloadCreated));
        _subscriptions.Add(_eventBus.Subscribe<WorkloadRenamedEvent>(OnWorkloadRenamed));
        _subscriptions.Add(_eventBus.Subscribe<WorkloadDeletedEvent>(OnWorkloadDeleted));
        // SessionMovedEvent事件在Week 10实现

        // 加载 Workloads
        _ = LoadWorkloadsAsync();
    }

    /// <summary>
    /// Workload 列表
    /// </summary>
    public ObservableCollection<WorkloadItemViewModel> Workloads { get; }

    /// <summary>
    /// 当前选中的 Workload
    /// </summary>
    public WorkloadItemViewModel? SelectedWorkload
    {
        get => _selectedWorkload;
        set
        {
            if (_selectedWorkload != value)
            {
                if (_selectedWorkload != null)
                    _selectedWorkload.IsSelected = false;

                _selectedWorkload = value;

                if (_selectedWorkload != null)
                    _selectedWorkload.IsSelected = true;

                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否正在加载
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 创建 Workload 命令
    /// </summary>
    public ICommand CreateWorkloadCommand { get; }

    /// <summary>
    /// 重命名 Workload 命令
    /// </summary>
    public ICommand RenameWorkloadCommand { get; }

    /// <summary>
    /// 删除 Workload 命令
    /// </summary>
    public ICommand DeleteWorkloadCommand { get; }

    /// <summary>
    /// 加载所有 Workloads
    /// </summary>
    public async Task LoadWorkloadsAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogInformation("Loading workloads...");

            var workloads = await _workloadService.GetAllWorkloadsAsync();

            Workloads.Clear();
            foreach (var workload in workloads.OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name))
            {
                Workloads.Add(new WorkloadItemViewModel(workload));
            }

            // 默认选中第一个（默认 Workload）
            if (Workloads.Count > 0)
            {
                SelectedWorkload = Workloads[0];
            }

            _logger.LogInformation("Loaded {Count} workloads", Workloads.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workloads");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 创建新 Workload
    /// </summary>
    private async Task CreateWorkloadAsync()
    {
        try
        {
            _logger.LogInformation("Creating new workload...");

            // 显示创建对话框 - 必须在UI线程上执行
            ComCross.Shell.Views.CreateWorkloadResult? result = null;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ComCross.Shell.Views.CreateWorkloadDialog(_localizedStrings);
                result = await dialog.ShowDialog<ComCross.Shell.Views.CreateWorkloadResult?>(
                    Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow!
                        : null!);
            });

            if (result != null)
            {
                var workload = await _workloadService.CreateWorkloadAsync(result.Name, result.Description);
                _logger.LogInformation("Created workload: {Name} ({Id})", workload.Name, workload.Id);
            }
            else
            {
                _logger.LogInformation("Workload creation cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workload");
        }
    }

    /// <summary>
    /// 重命名 Workload
    /// </summary>
    private async Task RenameWorkloadAsync()
    {
        if (SelectedWorkload == null)
            return;

        try
        {
            var workloadId = SelectedWorkload.Id;
            var currentName = SelectedWorkload.Name;

            _logger.LogInformation("Renaming workload: {Id}", workloadId);

            // 显示重命名对话框 - 必须在UI线程上执行
            string? newName = null;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ComCross.Shell.Views.RenameWorkloadDialog(_localizedStrings, currentName);
                newName = await dialog.ShowDialog<string?>(
                    Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow!
                        : null!);
            });

            if (!string.IsNullOrEmpty(newName))
            {
                await _workloadService.RenameWorkloadAsync(workloadId, newName);
                _logger.LogInformation("Renamed workload to: {NewName}", newName);
            }
            else
            {
                _logger.LogInformation("Workload rename cancelled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename workload");
        }
    }

    /// <summary>
    /// 删除 Workload
    /// </summary>
    private async Task DeleteWorkloadAsync()
    {
        if (SelectedWorkload == null)
            return;

        try
        {
            var workloadId = SelectedWorkload.Id;

            if (SelectedWorkload.IsDefault)
            {
                _logger.LogWarning("Cannot delete default workload");
                return;
            }

            _logger.LogInformation("Deleting workload: {Name} ({Id})", SelectedWorkload.Name, workloadId);

            var (success, message) = await _workloadService.DeleteWorkloadAsync(workloadId);
            
            if (!success)
            {
                _logger.LogWarning("Failed to delete workload: {Message}", message);
            }
            else
            {
                _logger.LogInformation("Deleted workload successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workload");
        }
    }

    // Week 10: Session移动功能
    // private async Task MoveSessionAsync(string sessionId, string targetWorkloadId) { }

    // 事件处理

    private void OnWorkloadCreated(WorkloadCreatedEvent e)
    {
        _logger.LogDebug("Workload created event received: {Id}", e.WorkloadId);
        _ = LoadWorkloadsAsync(); // 重新加载列表
    }

    private void OnWorkloadRenamed(WorkloadRenamedEvent e)
    {
        _logger.LogDebug("Workload renamed event received: {Id} -> {NewName}", e.WorkloadId, e.NewName);
        
        var workload = Workloads.FirstOrDefault(w => w.Id == e.WorkloadId);
        if (workload != null)
        {
            workload.UpdateName(e.NewName);
        }
    }

    private void OnWorkloadDeleted(WorkloadDeletedEvent e)
    {
        _logger.LogDebug("Workload deleted event received: {Id}", e.WorkloadId);
        
        var workload = Workloads.FirstOrDefault(w => w.Id == e.WorkloadId);
        if (workload != null)
        {
            Workloads.Remove(workload);
        }
    }

    // NOTE: Session移动功能在Week 10实现
    // private void OnSessionMoved(SessionMovedEvent e)
    // {
    //     _logger.LogDebug("Session moved event received: {SessionId} -> {TargetWorkloadId}", 
    //         e.SessionId, e.TargetWorkloadId);
    //     
    //     _ = LoadWorkloadsAsync(); // 重新加载列表（简单实现）
    // }

    public void Dispose()
    {
        // 取消订阅事件
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
