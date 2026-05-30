namespace MultiAgent.Core.Models;

/// <summary>
/// One row per agent attempt: name, step, input/output, status, duration, retry count, error. The audit trail.
/// </summary> 
public sealed class AgentTrace
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string? Output { get; set; }
    public RunStatus Status { get; set; }
    public long DurationMs { get; set; }
    public int RetryCount { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}
