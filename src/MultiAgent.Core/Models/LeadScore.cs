namespace MultiAgent.Core.Models;

/// <summary>
/// Qualification agent output: score, priority, industry, buying intent, urgency, reason.
/// </summary>
public sealed record LeadScore
{
    public int Score { get; init; }
    public Priority Priority { get; init; }
    public string Industry { get; init; } = string.Empty;
    public string BuyingIntent { get; init; } = string.Empty;
    public string Urgency { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
