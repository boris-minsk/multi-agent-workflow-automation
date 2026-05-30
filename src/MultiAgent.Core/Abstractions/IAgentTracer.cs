using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
/// Record one trace row per agent attempt and fetch all traces for a run (observability/audit).
/// </summary>
public interface IAgentTracer
{
    Task RecordAsync(AgentTrace trace, CancellationToken ct);
    Task<IReadOnlyList<AgentTrace>> GetByRunAsync(Guid runId, CancellationToken ct);
}
