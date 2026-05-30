using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
///  The entry point: start a run for a lead (returns immediately, executes in the background), then get/list runs.
/// </summary>
public interface IWorkflowRunner
{
    /// <summary>
    /// Starts a workflow run for the given lead and returns the run id immediately.
    /// The actual workflow executes on a background task.
    /// </summary>
    Task<Guid> StartAsync(Guid leadId, CancellationToken ct);

    Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken ct);

    Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(int take, CancellationToken ct);
}
