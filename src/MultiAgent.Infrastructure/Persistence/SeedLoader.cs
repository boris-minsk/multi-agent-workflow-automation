using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class SeedLoader(
    AppDbContext db,
    IOptions<PathsOptions> pathsOptions,
    ILogger<SeedLoader> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SeedIfEmptyAsync(CancellationToken ct)
    {
        var paths = pathsOptions.Value;
        var hasLeads = await db.Leads.AnyAsync(ct);

        if (!hasLeads)
        {
            await SeedLeadsAsync(paths.SeedDataFile, ct);
        }

        var hasResearch = await db.CompanyResearch.AnyAsync(ct);
        if (!hasResearch)
        {
            await SeedResearchAsync(paths.SeedResearchFile, ct);
        }
    }

    private async Task SeedLeadsAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Seed leads file not found at {Path}; skipping seed.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var leads = await JsonSerializer.DeserializeAsync<List<Lead>>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException("Seed leads JSON was empty or invalid.");

        var now = DateTime.UtcNow;
        foreach (var lead in leads)
        {
            if (lead.CreatedAt == default) lead.CreatedAt = now;
            if (lead.UpdatedAt == default) lead.UpdatedAt = now;
        }

        db.Leads.AddRange(leads);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} leads from {Path}.", leads.Count, path);
    }

    private async Task SeedResearchAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Seed research file not found at {Path}; skipping seed.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<CompanyResearchEntity>>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException("Seed research JSON was empty or invalid.");

        db.CompanyResearch.AddRange(entries);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} company research entries from {Path}.", entries.Count, path);
    }
}
