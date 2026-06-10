using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    public int QualificationThreshold { get; set; } = 5;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>
    /// When a qualifying lead's outreach email needs human approval before sending.
    /// Defaults to <see cref="ApprovalMode.Always"/> (a real system does not auto-send AI-written
    /// emails to prospects). Set to <see cref="ApprovalMode.Never"/> for the original auto-send flow.
    /// </summary>
    public ApprovalMode ApprovalMode { get; set; } = ApprovalMode.Always;

    /// <summary>How many runs the background worker processes concurrently.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;
}
