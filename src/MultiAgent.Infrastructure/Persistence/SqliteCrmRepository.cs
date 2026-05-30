using Microsoft.EntityFrameworkCore;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class SqliteCrmRepository(AppDbContext db) : ICrmRepository
{
    public async Task<Lead?> GetAsync(Guid id, CancellationToken ct) =>
        await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<Lead>> ListAsync(CancellationToken ct) =>
        await db.Leads.AsNoTracking().OrderBy(l => l.CompanyName).ToListAsync(ct);

    public async Task UpdateScoreAsync(Guid id, int score, Priority priority, string reason, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new InvalidOperationException($"Lead {id} not found");
        lead.Score = score;
        lead.Priority = priority;
        lead.ScoreReason = reason;
        lead.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStageAsync(Guid id, LeadStage stage, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new InvalidOperationException($"Lead {id} not found");
        lead.Stage = stage;
        lead.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddNoteAsync(Guid id, string note, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new InvalidOperationException($"Lead {id} not found");
        var separator = string.IsNullOrEmpty(lead.CrmNotes) ? "" : "\n---\n";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
        lead.CrmNotes = $"{lead.CrmNotes}{separator}[{timestamp}] {note}";
        lead.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> GetCrmHistoryAsync(Guid id, CancellationToken ct)
    {
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        return lead?.CrmNotes ?? string.Empty;
    }
}
