using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// Background service that drains <see cref="WorkflowQueue"/> and runs each work item on its own DI
/// scope (<see cref="SalesFollowUpWorkflow"/> is scoped). Runs <see cref="WorkflowRecovery"/> once at
/// startup — after the database initializer has migrated the DB; see the registration-order note in
/// Program.cs — then fans out across <c>Workflow:MaxConcurrentRuns</c> consumers. Replaces the old
/// fire-and-forget Task.Run, so work is dispatched from one place and survives a process restart.
/// </summary>
public sealed class WorkflowWorker(
    IServiceScopeFactory scopeFactory,
    WorkflowQueue queue,
    WorkflowRecovery recovery,
    IOptions<WorkflowOptions> options,
    ILogger<WorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await recovery.RecoverAsync(stoppingToken);

        var consumers = Math.Max(1, options.Value.MaxConcurrentRuns);
        await Task.WhenAll(Enumerable.Range(0, consumers).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — stop pulling new items.
        }
    }

    private async Task ProcessAsync(WorkItem item)
    {
        // CancellationToken.None: once an item is picked up, let it finish even during shutdown rather
        // than aborting mid-send (which would trip the workflow's cancel→Failed path).
        try
        {
            using var scope = scopeFactory.CreateScope();
            var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();

            var run = await runStore.GetAsync(item.RunId, CancellationToken.None);
            if (run is null || IsTerminal(run.Status))
            {
                // Stale or duplicate item, or the run already finished — nothing to do.
                return;
            }

            var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
            switch (item.Kind)
            {
                case WorkItemKind.Start:
                    await workflow.RunAsync(item.RunId, item.LeadId, CancellationToken.None);
                    break;
                case WorkItemKind.Resume:
                    await workflow.ResumeAfterApprovalAsync(item.RunId, item.LeadId, CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            // RunAsync/ResumeAfterApprovalAsync self-handle their own failures; reaching here means the
            // item itself couldn't be processed (e.g. scope/DI failure). Mark the run Failed so it
            // doesn't hang — and guard that write so a failing recovery can't kill the consumer loop.
            logger.LogError(ex, "Workflow item for run {RunId} ({Kind}) threw before/around the workflow handler.",
                item.RunId, item.Kind);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
                await runStore.SetStatusAsync(item.RunId, RunStatus.Failed, ex.Message, null, null, CancellationToken.None);
            }
            catch (Exception inner)
            {
                logger.LogError(inner, "Also failed to mark run {RunId} as Failed.", item.RunId);
            }
        }
    }

    private static bool IsTerminal(RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Skipped or RunStatus.Rejected;
}
