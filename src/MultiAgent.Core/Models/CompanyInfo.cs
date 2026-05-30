namespace MultiAgent.Core.Models;

/// <summary>
/// Research input fed into the agent: description, known pain points, recent news (from ICompanyResearchSource).
/// </summary>
public sealed record CompanyInfo
{
    public string Website { get; init; } = string.Empty;
    public string CompanyDescription { get; init; } = string.Empty;
    public List<string> KnownPainPoints { get; init; } = [];
    public List<string> RecentNews { get; init; } = [];
}
