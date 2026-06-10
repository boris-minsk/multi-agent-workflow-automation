using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// Coordinator (singleton, <see cref="IWorkflowRunner"/>): records a new <see cref="WorkflowRun"/>
/// and enqueues it onto <see cref="WorkflowQueue"/> for the background <see cref="WorkflowWorker"/> to
/// execute, exposes status queries, and handles the human-approval approve/reject actions. Enqueuing
/// (rather than a fire-and-forget Task.Run) means work is dispatched from one place and survives a
/// restart via the persisted run rows + <see cref="WorkflowRecovery"/>.
/// </summary>
public sealed class WorkflowRunner(
    IServiceScopeFactory scopeFactory,
    WorkflowQueue queue,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    public async Task<Guid> StartAsync(Guid leadId, CancellationToken ct)
    {
        Guid runId;
        using (var scope = scopeFactory.CreateScope())
        {
            var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
            var run = await runStore.CreateAsync(leadId, ct);
            runId = run.Id;
        }

        queue.Enqueue(new WorkItem(runId, leadId, WorkItemKind.Start));
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

        queue.Enqueue(new WorkItem(runId, leadId, WorkItemKind.Resume));
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
        // so it runs inline rather than on the background worker.
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
}
