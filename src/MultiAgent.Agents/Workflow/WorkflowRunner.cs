using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// Singleton coordinator that records a new <see cref="WorkflowRun"/>, fires off the
/// <see cref="SalesFollowUpWorkflow"/> on a background task with its own DI scope, and
/// exposes status queries. Phase 1 keeps runs in-memory; Phase 3 would swap this for
/// a durable queue.
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
}
