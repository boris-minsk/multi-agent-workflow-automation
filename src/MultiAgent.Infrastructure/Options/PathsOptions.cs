namespace MultiAgent.Infrastructure.Options;

public sealed class PathsOptions
{
    public const string SectionName = "Paths";

    public string OutboxDirectory { get; set; } = "outbox";
    public string NotesDirectory { get; set; } = "notes";
    public string SeedDataFile { get; set; } = "data/seed-leads.json";
    public string SeedResearchFile { get; set; } = "data/seed-research.json";
    public string SqliteDb { get; set; } = "data/multiagent.db";
}
