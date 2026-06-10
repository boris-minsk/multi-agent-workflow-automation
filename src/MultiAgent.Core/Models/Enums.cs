namespace MultiAgent.Core.Models;

public enum LeadStage
{
    New,
    Qualified,
    Contacted,
    Replied,
    Disqualified
}

public enum Priority
{
    Low,
    Medium,
    High
}

public enum RunStatus
{
    Pending,
    Running,
    AwaitingApproval,
    Completed,
    Failed,
    Skipped,
    Rejected
}

/// <summary>
/// Whether a qualifying lead's outreach email must be approved by a human before it is sent.
/// </summary>
public enum ApprovalMode
{
    /// <summary>Every qualifying lead waits for approval (production-faithful default).</summary>
    Always,

    /// <summary>Only high-value leads (High priority or score &gt;= 8) wait; routine leads auto-send.</summary>
    HighValueOnly,

    /// <summary>Auto-send everything; no approval step (original behavior).</summary>
    Never
}

public enum NotificationSeverity
{
    Info,
    Warning,
    Error
}
