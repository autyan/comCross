using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for Workload tabs management
/// </summary>
public sealed class WorkloadTabsViewModel : BaseViewModel, IDisposable
{
    private readonly WorkloadService _workloadService;
    private readonly IEventBus _eventBus;
    private WorkloadTabItemViewModel? _activeTab;

    public WorkloadTabsViewModel(
        ILocalizationService localization,
        WorkloadService workloadService,
        IEventBus eventBus)
        : base(localization)
    {
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        Tabs = new ObservableCollection<WorkloadTabItemViewModel>();

        // Create commands using AsyncRelayCommand and RelayCommand (same as MainWindowViewModel)
        CreateWorkloadCommand = new AsyncRelayCommand(CreateWorkloadAsync);
        ActivateWorkloadCommand = new RelayCommand<string>(ActivateWorkload);
        CloseWorkloadCommand = new AsyncRelayCommand<string>(CloseWorkloadAsync);
        RenameWorkloadCommand = new AsyncRelayCommand<string>(RenameWorkloadAsync);
        CopyWorkloadCommand = new AsyncRelayCommand<string>(CopyWorkloadAsync);

        _eventBus.Subscribe<WorkloadCreatedEvent>(OnWorkloadCreated);
        _eventBus.Subscribe<WorkloadRenamedEvent>(OnWorkloadRenamed);
        _eventBus.Subscribe<WorkloadDeletedEvent>(OnWorkloadDeleted);
    }

    /// <summary>
    /// Collection of workload tabs
    /// </summary>
    public ObservableCollection<WorkloadTabItemViewModel> Tabs { get; }

    /// <summary>
    /// Currently active tab
    /// </summary>
    public WorkloadTabItemViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (_activeTab != value)
            {
                if (_activeTab != null)
                    _activeTab.IsActive = false;

                _activeTab = value;

                if (_activeTab != null)
                    _activeTab.IsActive = true;

                OnPropertyChanged();
            }
        }
    }

    public ICommand CreateWorkloadCommand { get; }
    public ICommand ActivateWorkloadCommand { get; }
    public ICommand CloseWorkloadCommand { get; }
    public ICommand RenameWorkloadCommand { get; }
    public ICommand CopyWorkloadCommand { get; }

    /// <summary>
    /// Load all workloads and populate tabs
    /// </summary>
    public async Task LoadWorkloadsAsync()
    {
        var workloads = await _workloadService.GetAllWorkloadsAsync();

        Tabs.Clear();
        foreach (var workload in workloads)
        {
            var tabItem = new WorkloadTabItemViewModel(
                workload,
                ActivateWorkloadCommand,
                CloseWorkloadCommand,
                RenameWorkloadCommand,
                CopyWorkloadCommand
            );
            Tabs.Add(tabItem);
        }

        // Activate the default workload or first one
        var activeWorkloadId = await _workloadService.GetActiveWorkloadIdAsync();
        var activeTab = Tabs.FirstOrDefault(t => t.Id == activeWorkloadId) ?? Tabs.FirstOrDefault();
        if (activeTab != null)
        {
            ActiveTab = activeTab;
        }
    }

    /// <summary>
    /// Create a new workload
    /// </summary>
    private async Task CreateWorkloadAsync()
    {
        var dialog = App.ServiceProvider.GetRequiredService<CreateWorkloadDialog>();
        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow 
            : null;
        
        if (mainWindow == null)
            return;

        var result = await dialog.ShowDialog<CreateWorkloadResult?>(mainWindow);

        if (result != null)
        {
            await _workloadService.CreateWorkloadAsync(result.Name, result.Description);
        }
    }

    /// <summary>
    /// Activate a workload (switch to it)
    /// </summary>
    private void ActivateWorkload(string? workloadId)
    {
        if (string.IsNullOrEmpty(workloadId))
            return;

        var tab = Tabs.FirstOrDefault(t => t.Id == workloadId);
        if (tab != null && tab != ActiveTab)
        {
            ActiveTab = tab;
            _workloadService.SetActiveWorkload(workloadId);
            
            // Notify that active workload has changed (so statistics can be updated)
            _eventBus.Publish(new ActiveWorkloadChangedEvent(workloadId));
        }
    }

    /// <summary>
    /// Close a workload tab
    /// </summary>
    private async Task CloseWorkloadAsync(string? workloadId)
    {
        if (string.IsNullOrEmpty(workloadId))
            return;

        var tab = Tabs.FirstOrDefault(t => t.Id == workloadId);
        if (tab == null || !tab.CanClose)
            return;

        // TODO: Show confirmation dialog if workload has active sessions
        
        Tabs.Remove(tab);

        // If closed tab was active, activate another tab
        if (tab == ActiveTab)
        {
            var defaultTab = Tabs.FirstOrDefault(t => t.IsDefault);
            if (defaultTab != null)
            {
                ActivateWorkload(defaultTab.Id);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Rename a workload
    /// </summary>
    private async Task RenameWorkloadAsync(string? workloadId)
    {
        if (string.IsNullOrEmpty(workloadId))
            return;

        var tab = Tabs.FirstOrDefault(t => t.Id == workloadId);
        if (tab == null || !tab.CanRename)
            return;

        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow 
            : null;
        
        if (mainWindow == null)
            return;

        var dialog = App.ServiceProvider.GetRequiredService<RenameWorkloadDialog>();
        dialog.CurrentName = tab.Name;

        var result = await dialog.ShowDialog<string?>(mainWindow);

        if (!string.IsNullOrWhiteSpace(result) && result != tab.Name)
        {
            await _workloadService.RenameWorkloadAsync(workloadId, result);
        }
    }

    /// <summary>
    /// Copy a workload (state + data)
    /// </summary>
    private async Task CopyWorkloadAsync(string? workloadId)
    {
        if (string.IsNullOrEmpty(workloadId))
            return;

        var sourceTab = Tabs.FirstOrDefault(t => t.Id == workloadId);
        if (sourceTab == null || !sourceTab.CanCopy)
            return;

        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow 
            : null;
        
        if (mainWindow == null)
            return;

        // Show dialog to input new name
        var dialog = App.ServiceProvider.GetRequiredService<CreateWorkloadDialog>();
        var result = await dialog.ShowDialog<CreateWorkloadResult?>(mainWindow);

        if (result != null)
        {
            // Copy workload with new name
            await _workloadService.CopyWorkloadAsync(workloadId, result.Name);
        }
    }

    private void OnWorkloadCreated(WorkloadCreatedEvent e)
    {
        var workload = _workloadService.GetWorkload(e.WorkloadId);
        if (workload != null)
        {
            var tabItem = new WorkloadTabItemViewModel(
                workload,
                ActivateWorkloadCommand,
                CloseWorkloadCommand,
                RenameWorkloadCommand,
                CopyWorkloadCommand
            );
            Tabs.Add(tabItem);

            // Auto-activate new workload
            ActivateWorkload(workload.Id);
        }
    }

    private void OnWorkloadRenamed(WorkloadRenamedEvent e)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == e.WorkloadId);
        if (tab != null)
        {
            tab.Name = e.NewName;
        }
    }

    private void OnWorkloadDeleted(WorkloadDeletedEvent e)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == e.WorkloadId);
        if (tab != null)
        {
            Tabs.Remove(tab);

            // If deleted tab was active, activate default
            if (tab == ActiveTab)
            {
                var defaultTab = Tabs.FirstOrDefault(t => t.IsDefault);
                if (defaultTab != null)
                {
                    ActivateWorkload(defaultTab.Id);
                }
            }
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
