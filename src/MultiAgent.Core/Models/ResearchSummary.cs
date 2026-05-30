namespace MultiAgent.Core.Models;

/// <summary>
/// Research agent output: description, pain points, news, plus suggested outreach angles.
/// </summary>
public sealed record ResearchSummary
{
    public string CompanyDescription { get; init; } = string.Empty;
    public List<string> PainPoints { get; init; } = [];
    public List<string> RecentNews { get; init; } = [];
    public List<string> SuggestedAngles { get; init; } = [];
}
