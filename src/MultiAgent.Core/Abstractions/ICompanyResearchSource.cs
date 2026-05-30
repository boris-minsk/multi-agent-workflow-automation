using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

public interface ICompanyResearchSource
{
    Task<CompanyInfo?> GetCompanyAsync(string website, CancellationToken ct);
}
