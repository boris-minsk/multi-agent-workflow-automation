using Microsoft.EntityFrameworkCore;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class SqliteCompanyResearchSource(AppDbContext db) : ICompanyResearchSource
{
    public async Task<CompanyInfo?> GetCompanyAsync(string website, CancellationToken ct)
    {
        var e = await db.CompanyResearch
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Website == website, ct);
        return e is null ? null : new CompanyInfo
        {
            Website = e.Website,
            CompanyDescription = e.CompanyDescription,
            KnownPainPoints = e.KnownPainPoints,
            RecentNews = e.RecentNews
        };
    }
}
