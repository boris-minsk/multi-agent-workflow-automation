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

    /// <summary>
    /// Atomically move a run from one status to another. Returns true only if the row was in
    /// <paramref name="from"/> and was updated — this is the guard against a double-approve or two
    /// concurrent transitions. Stamps <c>CompletedAt</c> when <paramref name="to"/> is terminal.
    /// </summary>
    Task<bool> TryTransitionAsync(Guid runId, RunStatus from, RunStatus to, CancellationToken ct);

    /// <summary>Park a run for human approval: set status to AwaitingApproval and save the resume state.</summary>
    Task SetAwaitingApprovalAsync(Guid runId, string pendingStateJson, CancellationToken ct);

    /// <summary>Replace the saved approval state — used when a reviewer edits the draft before approving.</summary>
    Task UpdatePendingStateAsync(Guid runId, string pendingStateJson, CancellationToken ct);

    Task<WorkflowRun?> GetAsync(Guid runId, CancellationToken ct);

    Task<IReadOnlyList<WorkflowRun>> ListAsync(int take, CancellationToken ct);

    /// <summary>
    /// Runs in a non-terminal state (Pending, Running, AwaitingApproval) — used by startup recovery
    /// to find work that a previous process left unfinished.
    /// </summary>
    Task<IReadOnlyList<WorkflowRun>> ListNonTerminalAsync(CancellationToken ct);
}
