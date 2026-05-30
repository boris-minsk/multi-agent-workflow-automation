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
}
