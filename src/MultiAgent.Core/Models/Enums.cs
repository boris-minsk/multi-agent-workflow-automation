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
    Completed,
    Failed,
    Skipped
}

public enum NotificationSeverity
{
    Info,
    Warning,
    Error
}
