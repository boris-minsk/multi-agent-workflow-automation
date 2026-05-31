using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Tests.TestFixtures;

/// <summary>
/// In-memory <see cref="ICrmRepository"/> that records writes, for exercising the CRM tool
/// surfaces (the in-app agent tools and the MCP tools) without a database or HubSpot.
/// </summary>
internal sealed class RecordingCrmRepository : ICrmRepository
{
    public List<(Guid Id, LeadStage Stage)> StageUpdates { get; } = [];
    public List<(Guid Id, string Note)> Notes { get; } = [];
    public List<(Guid Id, int Score, Priority Priority, string Reason)> ScoreUpdates { get; } = [];
    public Lead? LeadToReturn { get; set; }

    public Task<Lead?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(LeadToReturn);

    public Task<IReadOnlyList<Lead>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Lead>>(LeadToReturn is null ? [] : [LeadToReturn]);

    public Task UpdateScoreAsync(Guid id, int score, Priority priority, string reason, CancellationToken ct)
    {
        ScoreUpdates.Add((id, score, priority, reason));
        return Task.CompletedTask;
    }

    public Task UpdateStageAsync(Guid id, LeadStage stage, CancellationToken ct)
    {
        StageUpdates.Add((id, stage));
        return Task.CompletedTask;
    }

    public Task AddNoteAsync(Guid id, string note, CancellationToken ct)
    {
        Notes.Add((id, note));
        return Task.CompletedTask;
    }

    public Task<string> GetCrmHistoryAsync(Guid id, CancellationToken ct) => Task.FromResult(string.Empty);
}
