using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
/// The mock CRM (Customer Relationship Management, HubSpot stand-in). 
/// Read leads, list them, update a lead's score/priority, move its stage, append notes, and fetch its history.
/// </summary>
public interface ICrmRepository
{
    Task<Lead?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Lead>> ListAsync(CancellationToken ct);
    Task UpdateScoreAsync(Guid id, int score, Priority priority, string reason, CancellationToken ct);
    Task UpdateStageAsync(Guid id, LeadStage stage, CancellationToken ct);
    Task AddNoteAsync(Guid id, string note, CancellationToken ct);
    Task<string> GetCrmHistoryAsync(Guid id, CancellationToken ct);
}
