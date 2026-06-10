namespace MultiAgent.Core.Models;

/// <summary>
/// State saved when a run pauses for human email approval — enough to resume the send + CRM
/// update step after approval without re-running the qualification / research / outreach agents.
/// Persisted as JSON in <see cref="WorkflowRun.PendingStateJson"/>.
/// </summary>
public sealed record PendingApprovalState
{
    public required LeadScore Score { get; init; }
    public required ResearchSummary Research { get; init; }
    public required OutreachDraft Draft { get; init; }
}
