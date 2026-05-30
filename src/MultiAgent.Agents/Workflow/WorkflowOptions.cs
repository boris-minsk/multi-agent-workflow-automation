namespace MultiAgent.Agents.Workflow;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    public int QualificationThreshold { get; set; } = 5;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;
}
