namespace MultiAgent.Core.Models;

/// <summary>
/// Customer Relationship Management agent output: target stage + a short note + a longer Markdown note for the workflow to apply.
/// </summary>
public sealed record CrmUpdateInstructions
{
    public LeadStage TargetStage { get; init; }
    public string ShortNote { get; init; } = string.Empty;
    public string MarkdownNote { get; init; } = string.Empty;
}
