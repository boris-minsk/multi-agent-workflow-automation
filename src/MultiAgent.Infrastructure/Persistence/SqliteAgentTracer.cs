using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class SqliteAgentTracer(IServiceScopeFactory scopeFactory) : IAgentTracer
{
    public async Task RecordAsync(AgentTrace trace, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (trace.Id == Guid.Empty) trace.Id = Guid.NewGuid();
        db.AgentTraces.Add(trace);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentTrace>> GetByRunAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentTraces
            .AsNoTracking()
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct);
    }
}
