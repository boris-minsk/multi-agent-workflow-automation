using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
/// Persistence for a run: create it, update its status/result, get/list runs.
/// </summary>
public interface IWorkflowRunStore
{
    Task<WorkflowRun> CreateAsync(Guid leadId, CancellationToken ct);

    Task SetStatusAsync(
        Guid runId,
        RunStatus status,
        string? errorMessage,
        string? finalScoreJson,
        string? finalDraftJson,
        CancellationToken ct);

    Task<WorkflowRun?> GetAsync(Guid runId, CancellationToken ct);

    Task<IReadOnlyList<WorkflowRun>> ListAsync(int take, CancellationToken ct);
}
