namespace MultiAgent.Core.Models;

/// <summary>
/// Outreach agent output: email subject, body, next action.
/// </summary>
public sealed record OutreachDraft
{
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
}
