using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// Singleton coordinator that records a new <see cref="WorkflowRun"/>, fires off the
/// <see cref="SalesFollowUpWorkflow"/> on a background task with its own DI scope, and exposes
/// status queries plus the approve/reject actions for runs paused at AwaitingApproval.
/// Background tasks are in-memory (not restart-safe); a parked run's state, however, lives in the
/// run row, so approval survives a restart even though the in-flight task does not.
/// </summary>
public sealed class WorkflowRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public async Task<Guid> StartAsync(Guid leadId, CancellationToken ct)
    {
        Guid runId;
        using (var scope = scopeFactory.CreateScope())
        {
            var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
            var run = await runStore.CreateAsync(leadId, ct);
            runId = run.Id;
        }

        var task = Task.Run(async () =>
        {
            try
            {
                using var innerScope = scopeFactory.CreateScope();
                var workflow = innerScope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
                await workflow.RunAsync(runId, leadId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background workflow task threw outside its own handler for run {RunId}.", runId);
            }
            finally
            {
                _running.TryRemove(runId, out _);
            }
        }, CancellationToken.None);

        _running[runId] = task;
        return runId;
    }

    public async Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        return await runStore.GetAsync(runId, ct);
    }

    public async Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(int take, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        return await runStore.ListAsync(take, ct);
    }

    public async Task<bool> ApproveAsync(Guid runId, string? subject, string? body, CancellationToken ct)
    {
        Guid leadId;
        using (var scope = scopeFactory.CreateScope())
        {
            var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
            var run = await runStore.GetAsync(runId, ct);
            if (run is null || run.Status != RunStatus.AwaitingApproval || string.IsNullOrWhiteSpace(run.PendingStateJson))
            {
                return false;
            }
            leadId = run.LeadId;

            // Apply the reviewer's edits (if any) to the saved draft BEFORE the transition, so the
            // resume reads the edited version. Run is still AwaitingApproval, so nothing reads it as final.
            if (subject is not null || body is not null)
            {
                var pending = JsonSerializer.Deserialize<PendingApprovalState>(run.PendingStateJson, AgentJson.Options);
                if (pending is not null)
                {
                    var edited = pending.Draft with
                    {
                        Subject = subject ?? pending.Draft.Subject,
                        Body = body ?? pending.Draft.Body
                    };
                    await runStore.UpdatePendingStateAsync(
                        runId, JsonSerializer.Serialize(pending with { Draft = edited }, AgentJson.Options), ct);
                }
            }

            // Atomic guard: only one approval wins the AwaitingApproval -> Running move; a second
            // (double-click / concurrent) approval sees false and the caller returns 409.
            if (!await runStore.TryTransitionAsync(runId, RunStatus.AwaitingApproval, RunStatus.Running, ct))
            {
                return false;
            }
        }

        FireResume(runId, leadId);
        return true;
    }

    public async Task<bool> RejectAsync(Guid runId, string? reason, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var run = await runStore.GetAsync(runId, ct);
        if (run is null || run.Status != RunStatus.AwaitingApproval)
        {
            return false;
        }

        // Atomic guard, then record the rejection. Reject is fast + deterministic (no agent call),
        // so it runs inline rather than on a background task.
        if (!await runStore.TryTransitionAsync(runId, RunStatus.AwaitingApproval, RunStatus.Rejected, ct))
        {
            return false;
        }

        try
        {
            var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
            await workflow.ApplyRejectionAsync(run.LeadId, runId, reason, ct);
        }
        catch (Exception ex)
        {
            // The run is already Rejected; the note/notification is best-effort.
            logger.LogError(ex, "Run {RunId} marked Rejected but the rejection note/notification failed.", runId);
        }

        return true;
    }

    /// <summary>
    /// Runs Part B on a background task after an approval. The status was already moved to Running by
    /// <see cref="ApproveAsync"/>, so if the task cannot even start (scope/DI failure) this catch
    /// marks the run Failed — otherwise it would be stuck in Running with no live task to recover it.
    /// (ResumeAfterApprovalAsync self-handles failures that happen once the workflow is running.)
    /// </summary>
    private void FireResume(Guid runId, Guid leadId)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
                await workflow.ResumeAfterApprovalAsync(runId, leadId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Resume task for run {RunId} threw before/around the workflow handler.", runId);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
                    await runStore.SetStatusAsync(runId, RunStatus.Failed, ex.Message, null, null, CancellationToken.None);
                }
                catch (Exception inner)
                {
                    logger.LogError(inner, "Also failed to mark resumed run {RunId} as Failed.", runId);
                }
            }
            finally
            {
                _running.TryRemove(runId, out _);
            }
        }, CancellationToken.None);

        _running[runId] = task;
    }
}
