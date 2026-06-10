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

    /// <summary>
    /// Approve a run parked at AwaitingApproval and resume it (send → CRM update → complete) on a
    /// background task. Optional <paramref name="subject"/>/<paramref name="body"/> override the
    /// drafted email. Returns false if the run is not awaiting approval (e.g. already approved).
    /// </summary>
    Task<bool> ApproveAsync(Guid runId, string? subject, string? body, CancellationToken ct);

    /// <summary>
    /// Reject a run parked at AwaitingApproval: no email is sent and the run ends as Rejected.
    /// Returns false if the run is not awaiting approval.
    /// </summary>
    Task<bool> RejectAsync(Guid runId, string? reason, CancellationToken ct);
}
