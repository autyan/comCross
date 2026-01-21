using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ComCross.Core.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed record WorkloadItemContext(
    Workload Workload,
    ICommand RenameCommand,
    ICommand DeleteCommand);

public sealed record WorkloadSessionItemContext(
    string SessionId,
    string WorkloadId,
    string Name);

/// <summary>
/// Workload 项的 ViewModel，用于在 UI 中显示单个 Workload
/// </summary>
public sealed class WorkloadItemViewModel : LocalizedItemViewModelBase<WorkloadItemContext>
{
    private readonly IItemVmFactory<SessionItemViewModel, WorkloadSessionItemContext> _sessionItemFactory;

    private bool _isExpanded = true;
    private bool _isSelected;

    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private bool _isDefault;
    private DateTime _createdAt;
    private DateTime _updatedAt;

    public WorkloadItemViewModel(
        ILocalizationService localization,
        IItemVmFactory<SessionItemViewModel, WorkloadSessionItemContext> sessionItemFactory)
        : base(localization)
    {
        _sessionItemFactory = sessionItemFactory;

        Sessions = new ItemVmCollection<SessionItemViewModel, WorkloadSessionItemContext>(_sessionItemFactory);
    }

    protected override void OnInit(WorkloadItemContext context)
    {
        _id = context.Workload.Id;
        _name = context.Workload.Name;
        _description = context.Workload.Description ?? string.Empty;
        _isDefault = context.Workload.IsDefault;
        _createdAt = context.Workload.CreatedAt;
        _updatedAt = context.Workload.UpdatedAt;

        RenameCommand = context.RenameCommand;
        DeleteCommand = context.DeleteCommand;

        Sessions.Clear();
        foreach (var sessionId in context.Workload.SessionIds)
        {
            var displayName = sessionId.Length >= 8
                ? $"Session {sessionId.Substring(0, 8)}"
                : $"Session {sessionId}";

            Sessions.Add(new WorkloadSessionItemContext(
                sessionId,
                _id,
                displayName));
        }

        OnPropertyChanged(nameof(SessionCount));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Workload ID
    /// </summary>
    public string Id => _id;

    /// <summary>
    /// Workload 名称
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Workload 描述
    /// </summary>
    public string Description => _description;

    /// <summary>
    /// 是否为默认 Workload
    /// </summary>
    public bool IsDefault => _isDefault;

    /// <summary>
    /// 图标（默认 Workload 显示 🏠，普通 Workload 显示 📁）
    /// </summary>
    public string Icon => IsDefault ? "🏠" : "📁";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt => _updatedAt;

    /// <summary>
    /// Workload 中的 Session 列表
    /// </summary>
    public ItemVmCollection<SessionItemViewModel, WorkloadSessionItemContext> Sessions { get; }

    public ICommand RenameCommand { get; private set; } = null!;

    public ICommand DeleteCommand { get; private set; } = null!;

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
        if (_name != newName)
        {
            _name = newName;
            _updatedAt = DateTime.UtcNow;
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
        if (_description != newDescription)
        {
            _description = newDescription;
            _updatedAt = DateTime.UtcNow;
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(UpdatedAt));
        }
    }

    /// <summary>
    /// 添加 Session
    /// </summary>
    public void AddSession(WorkloadSessionItemContext session)
    {
        if (!Sessions.Any(s => string.Equals(s.Id, session.SessionId, StringComparison.Ordinal)))
        {
            Sessions.Add(session);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Sessions.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Session 项的 ViewModel（用于在 Workload 下显示）
/// </summary>
public sealed class SessionItemViewModel : INotifyPropertyChanged, IInitializable<WorkloadSessionItemContext>
{
    private string _name = string.Empty;
    private string _workloadId = string.Empty;
    private bool _isConnected;
    private bool _isInitialized;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Session ID
    /// </summary>
    public string Id { get; private set; } = string.Empty;

    public void Init(WorkloadSessionItemContext context)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("SessionItemViewModel already initialized.");
        }

        _isInitialized = true;
        Id = context.SessionId;
        _workloadId = context.WorkloadId;
        _name = context.Name;
        OnPropertyChanged(string.Empty);
    }

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
