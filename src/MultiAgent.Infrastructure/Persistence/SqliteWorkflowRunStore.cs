using Microsoft.EntityFrameworkCore;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class SqliteWorkflowRunStore(AppDbContext db) : IWorkflowRunStore
{
    public async Task<WorkflowRun> CreateAsync(Guid leadId, CancellationToken ct)
    {
        var run = new WorkflowRun
        {
            Id = Guid.NewGuid(),
            LeadId = leadId,
            Status = RunStatus.Pending,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task SetStatusAsync(
        Guid runId,
        RunStatus status,
        string? errorMessage,
        string? finalScoreJson,
        string? finalDraftJson,
        CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException($"WorkflowRun {runId} not found");
        run.Status = status;
        if (errorMessage is not null) run.ErrorMessage = errorMessage;
        if (finalScoreJson is not null) run.FinalScoreJson = finalScoreJson;
        if (finalDraftJson is not null) run.FinalDraftJson = finalDraftJson;
        if (IsTerminal(status))
        {
            run.CompletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryTransitionAsync(Guid runId, RunStatus from, RunStatus to, CancellationToken ct)
    {
        // Single conditional UPDATE: only the row currently in `from` is changed. SQLite serializes
        // writes, so concurrent approvals race here and exactly one wins (affected == 1); the rest
        // see 0. This is the guard against a double-send.
        var query = db.WorkflowRuns.Where(r => r.Id == runId && r.Status == from);
        int affected = IsTerminal(to)
            ? await query.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.CompletedAt, DateTime.UtcNow), ct)
            : await query.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to), ct);
        return affected == 1;
    }

    public async Task SetAwaitingApprovalAsync(Guid runId, string pendingStateJson, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException($"WorkflowRun {runId} not found");
        run.Status = RunStatus.AwaitingApproval;
        run.PendingStateJson = pendingStateJson;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePendingStateAsync(Guid runId, string pendingStateJson, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException($"WorkflowRun {runId} not found");
        run.PendingStateJson = pendingStateJson;
        await db.SaveChangesAsync(ct);
    }

    private static bool IsTerminal(RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Skipped or RunStatus.Rejected;

    public async Task<WorkflowRun?> GetAsync(Guid runId, CancellationToken ct) =>
        await db.WorkflowRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);

    public async Task<IReadOnlyList<WorkflowRun>> ListAsync(int take, CancellationToken ct) =>
        await db.WorkflowRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToListAsync(ct);
}
