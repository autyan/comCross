using System.Text.Json;
using ComCross.Core.Models;

namespace ComCross.Core.Services;

/// <summary>
/// Single owner for workspace-state.json.
/// All workspace persistence must flow through this store.
/// </summary>
public sealed class WorkspaceStateStore
{
    private readonly ConfigService _configService;
    private readonly WorkspaceMigrationService _migrationService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WorkspaceState? _state;

    public WorkspaceStateStore(
        ConfigService configService,
        WorkspaceMigrationService migrationService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
    }

    public async Task<WorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return Clone(_state!);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state = Clone(state);
            Normalize(_state);
            await _configService.SaveWorkspaceStateAsync(_state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<TResult>(
        Func<WorkspaceState, TResult> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var result = mutate(_state!);
            Normalize(_state!);
            await _configService.SaveWorkspaceStateAsync(_state!, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpdateAsync(Action<WorkspaceState> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return UpdateAsync(state =>
        {
            mutate(state);
            return true;
        }, cancellationToken);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return;
        }

        var state = await _configService.LoadWorkspaceStateAsync(cancellationToken) ?? new WorkspaceState();
        var changed = false;

        if (_migrationService.NeedsMigration(state))
        {
            state = _migrationService.Migrate(state);
            changed = true;
        }

        changed |= Normalize(state);
        _state = state;

        if (changed)
        {
            await _configService.SaveWorkspaceStateAsync(_state, cancellationToken);
        }
    }

    private static bool Normalize(WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var changed = false;

        if (string.IsNullOrWhiteSpace(state.Version))
        {
            state.Version = "0.4.0";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(state.WorkspaceId))
        {
            state.WorkspaceId = "default";
            changed = true;
        }

        if (state.Workloads is null)
        {
            state.Workloads = new List<Workload>();
            changed = true;
        }

        if (state.SessionDescriptors is null)
        {
            state.SessionDescriptors = new List<SessionDescriptor>();
            changed = true;
        }

        if (state.SendHistory is null)
        {
            state.SendHistory = new List<string>();
            changed = true;
        }

        if (state.GetDefaultWorkload() == null)
        {
            state.EnsureDefaultWorkload();
            changed = true;
        }

        var resolvedActiveWorkloadId = ResolveActiveWorkloadId(state);
        if (!string.Equals(state.ActiveWorkloadId, resolvedActiveWorkloadId, StringComparison.Ordinal))
        {
            state.ActiveWorkloadId = resolvedActiveWorkloadId;
            changed = true;
        }

        return changed;
    }

    private static string ResolveActiveWorkloadId(WorkspaceState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveWorkloadId)
            && state.Workloads.Any(w => string.Equals(w.Id, state.ActiveWorkloadId, StringComparison.Ordinal)))
        {
            return state.ActiveWorkloadId;
        }

        return state.GetDefaultWorkload()?.Id
            ?? state.Workloads.FirstOrDefault()?.Id
            ?? string.Empty;
    }

    private static WorkspaceState Clone(WorkspaceState state)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(state);
        return JsonSerializer.Deserialize<WorkspaceState>(json)
            ?? throw new InvalidOperationException("Failed to clone workspace state.");
    }
}
