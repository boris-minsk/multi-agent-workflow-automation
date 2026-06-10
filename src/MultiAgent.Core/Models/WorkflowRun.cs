namespace MultiAgent.Core.Models;

/// <summary>
/// One execution of the pipeline for a lead: status, start/end times, error, and the final score/draft snapshots as JSON.
/// </summary>
public sealed class WorkflowRun
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FinalScoreJson { get; set; }
    public string? FinalDraftJson { get; set; }

    /// <summary>
    /// Scratch state for a run parked at <see cref="RunStatus.AwaitingApproval"/>: the score,
    /// research, and the draft proposed for sending, as JSON. Distinct from the Final* fields,
    /// which record what was actually sent once the run completes.
    /// </summary>
    public string? PendingStateJson { get; set; }
}
