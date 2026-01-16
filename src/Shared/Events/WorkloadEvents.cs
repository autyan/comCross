namespace ComCross.Shared.Events;

/// <summary>
/// Event raised when a workload is created.
/// </summary>
/// <param name="WorkloadId">ID of the created workload</param>
/// <param name="WorkloadName">Name of the created workload</param>
public record WorkloadCreatedEvent(string WorkloadId, string WorkloadName);

/// <summary>
/// Event raised when a workload is deleted.
/// </summary>
/// <param name="WorkloadId">ID of the deleted workload</param>
/// <param name="WorkloadName">Name of the deleted workload</param>
public record WorkloadDeletedEvent(string WorkloadId, string WorkloadName);

/// <summary>
/// Event raised when a workload is renamed.
/// </summary>
/// <param name="WorkloadId">ID of the renamed workload</param>
/// <param name="OldName">Old name</param>
/// <param name="NewName">New name</param>
public record WorkloadRenamedEvent(string WorkloadId, string OldName, string NewName);

/// <summary>
/// Event raised when the active workload changes.
/// </summary>
/// <param name="WorkloadId">ID of the activated workload</param>
public record ActiveWorkloadChangedEvent(string WorkloadId);
