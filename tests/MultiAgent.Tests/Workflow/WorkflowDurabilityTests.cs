using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiAgent.Agents.Workflow;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Persistence;
using MultiAgent.Tests.TestFixtures;

namespace MultiAgent.Tests.Workflow;

/// <summary>
/// Durable runs: startup recovery re-queues unfinished work safely, the new store/email helpers it
/// relies on behave, and the runner now enqueues onto the background queue instead of Task.Run.
/// Tests drive the pieces directly (the worker is a hosted service that does not run in tests).
/// </summary>
public class WorkflowDurabilityTests
{
    [Fact]
    public async Task Recovery_RequeuesPending_ResumesUnsent_FailsSent_LeavesAwaitingAndTerminal()
    {
        await using var test = new TestServiceProvider();
        var pending = Guid.NewGuid();
        var runningUnsent = Guid.NewGuid();
        var runningUnsentWithPending = Guid.NewGuid();
        var runningSent = Guid.NewGuid();
        var awaiting = Guid.NewGuid();
        var completed = Guid.NewGuid();

        await SeedRunAsync(test, pending, RunStatus.Pending);
        await SeedRunAsync(test, runningUnsent, RunStatus.Running);
        await SeedRunAsync(test, runningUnsentWithPending, RunStatus.Running, pendingStateJson: "{}");
        await SeedRunAsync(test, runningSent, RunStatus.Running, withOutbox: true);
        await SeedRunAsync(test, awaiting, RunStatus.AwaitingApproval);
        await SeedRunAsync(test, completed, RunStatus.Completed);

        var recovery = test.Services.GetRequiredService<WorkflowRecovery>();
        var queue = test.Services.GetRequiredService<WorkflowQueue>();

        await recovery.RecoverAsync(CancellationToken.None);

        var queued = Drain(queue);
        Assert.Equal(3, queued.Count);
        Assert.Contains(queued, w => w.RunId == pending && w.Kind == WorkItemKind.Start);
        Assert.Contains(queued, w => w.RunId == runningUnsent && w.Kind == WorkItemKind.Start);
        Assert.Contains(queued, w => w.RunId == runningUnsentWithPending && w.Kind == WorkItemKind.Resume);
        Assert.DoesNotContain(queued, w => w.RunId == runningSent);
        Assert.DoesNotContain(queued, w => w.RunId == awaiting);
        Assert.DoesNotContain(queued, w => w.RunId == completed);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sent = await db.WorkflowRuns.SingleAsync(r => r.Id == runningSent);
        Assert.Equal(RunStatus.Failed, sent.Status);
        Assert.Contains("restart", sent.ErrorMessage);
        Assert.NotNull(sent.CompletedAt);

        // Untouched states.
        Assert.Equal(RunStatus.Pending, (await db.WorkflowRuns.SingleAsync(r => r.Id == pending)).Status);
        Assert.Equal(RunStatus.Running, (await db.WorkflowRuns.SingleAsync(r => r.Id == runningUnsent)).Status);
        Assert.Equal(RunStatus.AwaitingApproval, (await db.WorkflowRuns.SingleAsync(r => r.Id == awaiting)).Status);
        Assert.Equal(RunStatus.Completed, (await db.WorkflowRuns.SingleAsync(r => r.Id == completed)).Status);
    }

    [Fact]
    public async Task ListNonTerminalAsync_ReturnsOnlyNonTerminalRuns()
    {
        await using var test = new TestServiceProvider();
        var expected = new HashSet<Guid>();
        foreach (var s in new[] { RunStatus.Pending, RunStatus.Running, RunStatus.AwaitingApproval })
        {
            var id = Guid.NewGuid();
            await SeedRunAsync(test, id, s);
            expected.Add(id);
        }
        foreach (var s in new[] { RunStatus.Completed, RunStatus.Failed, RunStatus.Skipped, RunStatus.Rejected })
        {
            await SeedRunAsync(test, Guid.NewGuid(), s);
        }

        using var scope = test.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var result = await store.ListNonTerminalAsync(CancellationToken.None);

        Assert.Equal(expected, result.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task HasSentForRunAsync_TrueOnlyAfterSend()
    {
        await using var test = new TestServiceProvider();
        using var scope = test.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var sentRun = Guid.NewGuid();
        await sender.SendAsync(sentRun, Guid.NewGuid(), "x@example.com", "subject", "body", CancellationToken.None);

        Assert.True(await sender.HasSentForRunAsync(sentRun, CancellationToken.None));
        Assert.False(await sender.HasSentForRunAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_CreatesPendingRunAndEnqueuesStart()
    {
        await using var test = new TestServiceProvider();
        var runner = test.Services.GetRequiredService<IWorkflowRunner>();
        var queue = test.Services.GetRequiredService<WorkflowQueue>();
        var leadId = Guid.NewGuid();

        var runId = await runner.StartAsync(leadId, CancellationToken.None);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Pending, run.Status);   // worker doesn't run in tests, so it stays Pending

        var item = Assert.Single(Drain(queue));
        Assert.Equal(runId, item.RunId);
        Assert.Equal(leadId, item.LeadId);
        Assert.Equal(WorkItemKind.Start, item.Kind);
    }

    private static List<WorkItem> Drain(WorkflowQueue queue)
    {
        var items = new List<WorkItem>();
        while (queue.Reader.TryRead(out var item))
        {
            items.Add(item);
        }
        return items;
    }

    private static async Task SeedRunAsync(
        TestServiceProvider test, Guid id, RunStatus status, string? pendingStateJson = null, bool withOutbox = false)
    {
        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.WorkflowRuns.Add(new WorkflowRun
        {
            Id = id,
            LeadId = Guid.NewGuid(),
            Status = status,
            StartedAt = DateTime.UtcNow,
            PendingStateJson = pendingStateJson
        });
        if (withOutbox)
        {
            db.OutboxItems.Add(new OutboxItem
            {
                Id = Guid.NewGuid(),
                RunId = id,
                LeadId = Guid.NewGuid(),
                ToAddress = "x@example.com",
                Subject = "s",
                Body = "b",
                GeneratedAt = DateTime.UtcNow,
                FilePath = "x.eml"
            });
        }
        await db.SaveChangesAsync();
    }
}
