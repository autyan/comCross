using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ComCross.Core.Models;
using ComCross.Shared.Services;
using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// Workload 项的 ViewModel，用于在 UI 中显示单个 Workload
/// </summary>
public sealed class WorkloadItemViewModel : BaseViewModel
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public WorkloadItemViewModel(
        ILocalizationService localization,
        Workload workload,
        ICommand renameCommand,
        ICommand deleteCommand,
        IObjectFactory objectFactory)
        : base(localization)
    {
        Id = workload.Id;
        Name = workload.Name;
        Description = workload.Description ?? string.Empty;
        IsDefault = workload.IsDefault;
        CreatedAt = workload.CreatedAt;
        UpdatedAt = workload.UpdatedAt;

        RenameCommand = renameCommand;
        DeleteCommand = deleteCommand;
        
        Sessions = new ObservableCollection<SessionItemViewModel>();
        
        // 加载 Sessions（从 SessionIds）
        foreach (var sessionId in workload.SessionIds)
        {
            var session = objectFactory.Create<SessionItemViewModel>();
            session.Id = sessionId;
            session.Name = $"Session {sessionId.Substring(0, 8)}"; // 临时名称，稍后从实际 Session 加载
            session.WorkloadId = Id;
            Sessions.Add(session);
        }
    }

    /// <summary>
    /// Workload ID
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Workload 名称
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Workload 描述
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 是否为默认 Workload
    /// </summary>
    public bool IsDefault { get; }

    /// <summary>
    /// 图标（默认 Workload 显示 🏠，普通 Workload 显示 📁）
    /// </summary>
    public string Icon => IsDefault ? "🏠" : "📁";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Workload 中的 Session 列表
    /// </summary>
    public ObservableCollection<SessionItemViewModel> Sessions { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>
    /// Session 数量
    /// </summary>
    public int SessionCount => Sessions.Count;

    /// <summary>
    /// 显示的计数文本（如 "默认任务 (3)"）
    /// </summary>
    public string DisplayName => $"{Name} ({SessionCount})";

    public string RenameHeader => L["workload.rename"];

    public string DeleteHeader => L["workload.delete"];

    /// <summary>
    /// 是否展开（TreeView 折叠/展开状态）
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否选中（当前活动 Workload）
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 更新 Workload 名称（用于重命名）
    /// </summary>
    public void UpdateName(string newName)
    {
        if (Name != newName)
        {
            Name = newName;
            UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(UpdatedAt));
        }
    }

    /// <summary>
    /// 更新描述
    /// </summary>
    public void UpdateDescription(string newDescription)
    {
        if (Description != newDescription)
        {
            Description = newDescription;
            UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(UpdatedAt));
        }
    }

    /// <summary>
    /// 添加 Session
    /// </summary>
    public void AddSession(SessionItemViewModel session)
    {
        if (!Sessions.Contains(session))
        {
            Sessions.Add(session);
            session.WorkloadId = Id;
            OnPropertyChanged(nameof(SessionCount));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// 移除 Session
    /// </summary>
    public void RemoveSession(SessionItemViewModel session)
    {
        if (Sessions.Remove(session))
        {
            OnPropertyChanged(nameof(SessionCount));
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}

/// <summary>
/// Session 项的 ViewModel（用于在 Workload 下显示）
/// </summary>
public sealed class SessionItemViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _workloadId = string.Empty;
    private bool _isConnected;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Session ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Session 名称
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
            }
        }
    }

    /// <summary>
    /// 所属 Workload ID
    /// </summary>
    public string WorkloadId
    {
        get => _workloadId;
        set
        {
            if (_workloadId != value)
            {
                _workloadId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusIcon));
            }
        }
    }

    /// <summary>
    /// 状态图标（已连接显示绿点 🟢，未连接显示灰点 ⚪）
    /// </summary>
    public string StatusIcon => IsConnected ? "🟢" : "⚪";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
