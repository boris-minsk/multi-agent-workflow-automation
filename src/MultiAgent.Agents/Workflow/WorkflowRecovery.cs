using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// On startup, finds runs a previous process left unfinished and decides what to do with each:
/// re-queue not-yet-started work, resume interrupted work that had not sent its email yet, and fail
/// work that was interrupted after the email went out (so a restart never causes a duplicate send).
/// Host-independent (takes an <see cref="IServiceScopeFactory"/>) so it can be unit-tested directly.
/// </summary>
public sealed class WorkflowRecovery(
    IServiceScopeFactory scopeFactory,
    WorkflowQueue queue,
    ILogger<WorkflowRecovery> logger)
{
    public async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var runs = await runStore.ListNonTerminalAsync(ct);
        int requeued = 0, failed = 0, awaiting = 0;

        foreach (var run in runs)
        {
            switch (run.Status)
            {
                case RunStatus.Pending:
                    // Never started — no side-effects yet, safe to run from the top.
                    queue.Enqueue(new WorkItem(run.Id, run.LeadId, WorkItemKind.Start));
                    requeued++;
                    break;

                case RunStatus.Running when await emailSender.HasSentForRunAsync(run.Id, ct):
                    // The email already went out; auto-finishing risks a duplicate send, so fail cleanly.
                    await runStore.SetStatusAsync(run.Id, RunStatus.Failed,
                        "Interrupted by a server restart after the email was sent; not auto-resumed.",
                        null, null, ct);
                    failed++;
                    break;

                case RunStatus.Running:
                    // No email sent yet → safe to resume. Post-approval runs (PendingStateJson set)
                    // resume from the send step; the rest re-run from the top.
                    var kind = string.IsNullOrWhiteSpace(run.PendingStateJson)
                        ? WorkItemKind.Start
                        : WorkItemKind.Resume;
                    queue.Enqueue(new WorkItem(run.Id, run.LeadId, kind));
                    requeued++;
                    break;

                case RunStatus.AwaitingApproval:
                    // Durable pause — leave it; a reviewer can still approve it after the restart.
                    awaiting++;
                    break;
            }
        }

        if (runs.Count > 0)
        {
            logger.LogInformation(
                "Workflow recovery: {Requeued} re-queued, {Failed} failed (email already sent), {Awaiting} left awaiting approval.",
                requeued, failed, awaiting);
        }
    }
}
