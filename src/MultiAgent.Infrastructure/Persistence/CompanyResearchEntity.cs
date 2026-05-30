namespace MultiAgent.Infrastructure.Persistence;

internal sealed class CompanyResearchEntity
{
    public string Website { get; set; } = string.Empty;
    public string CompanyDescription { get; set; } = string.Empty;
    public List<string> KnownPainPoints { get; set; } = [];
    public List<string> RecentNews { get; set; } = [];
}
