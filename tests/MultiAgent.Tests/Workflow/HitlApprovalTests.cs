using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiAgent.Agents.Workflow;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Persistence;
using MultiAgent.Tests.TestFixtures;

namespace MultiAgent.Tests.Workflow;

/// <summary>
/// Human-in-the-loop email approval: a qualifying lead pauses (AwaitingApproval) before the email
/// is sent, and a reviewer approves (optionally editing) or rejects. Tests drive the workflow
/// directly (Part A via RunAsync, resume via the store transition + ResumeAfterApprovalAsync) for
/// determinism, matching the existing workflow tests.
/// </summary>
public class HitlApprovalTests
{
    private const string HotNotes = "VP booked a demo. Procurement said budget is approved. Timeline 30 days.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Always_PausesAwaitingApproval()
    {
        await using var test = new TestServiceProvider(c => c["Workflow:ApprovalMode"] = "Always");
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka", HotNotes);
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunPartAAsync(test, lead.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.AwaitingApproval, run.Status);
        Assert.Null(run.CompletedAt);
        Assert.False(string.IsNullOrWhiteSpace(run.PendingStateJson),
            "pending state should be saved while awaiting approval");

        Assert.Empty(await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync());

        var warnings = await db.Notifications
            .Where(n => n.RunId == runId && n.Severity == NotificationSeverity.Warning)
            .ToListAsync();
        Assert.NotEmpty(warnings);

        var updated = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.NotEqual(LeadStage.Contacted, updated.Stage);
    }

    [Fact]
    public async Task Approve_ResumesAndSendsEmail()
    {
        await using var test = new TestServiceProvider(c => c["Workflow:ApprovalMode"] = "Always");
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka", HotNotes);
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunPartAAsync(test, lead.Id);
        await ApproveAndResumeAsync(test, runId, lead.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Completed, run.Status);

        var email = Assert.Single(await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync());
        Assert.False(string.IsNullOrWhiteSpace(email.Body));

        var updated = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal(LeadStage.Contacted, updated.Stage);
    }

    [Fact]
    public async Task ApproveWithEdit_SendsEditedBody()
    {
        await using var test = new TestServiceProvider(c => c["Workflow:ApprovalMode"] = "Always");
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka", HotNotes);
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunPartAAsync(test, lead.Id);

        const string editedSubject = "Edited by reviewer: let's talk";
        const string editedBody = "Hi Yuki, the reviewer rewrote this body before sending. Cheers.";
        await ApproveAndResumeAsync(test, runId, lead.Id, editedSubject, editedBody);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = Assert.Single(await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync());
        Assert.Equal(editedSubject, email.Subject);
        Assert.Equal(editedBody, email.Body);

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.FinalDraftJson);                 // records what was actually sent
        Assert.Contains("reviewer rewrote", run.FinalDraftJson!);
    }

    [Fact]
    public async Task Reject_MarksRejectedAndSendsNoEmail()
    {
        await using var test = new TestServiceProvider(c => c["Workflow:ApprovalMode"] = "Always");
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka", HotNotes);
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunPartAAsync(test, lead.Id);

        using (var actScope = test.CreateScope())
        {
            var runner = actScope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
            Assert.True(await runner.RejectAsync(runId, "Tone is off; not personalized enough.", CancellationToken.None));
            // A second reject is a no-op — the run is already terminal (guards against double action).
            Assert.False(await runner.RejectAsync(runId, "again", CancellationToken.None));
        }

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Rejected, run.Status);
        Assert.NotNull(run.CompletedAt);

        Assert.Empty(await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync());

        var updated = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal(LeadStage.New, updated.Stage);          // stage intentionally left unchanged
        Assert.Contains("rejected by reviewer", updated.CrmNotes);

        var info = await db.Notifications
            .Where(n => n.RunId == runId && n.Severity == NotificationSeverity.Info)
            .ToListAsync();
        Assert.NotEmpty(info);
    }

    [Fact]
    public async Task HighValueOnly_PausesHighValueButAutoSendsRoutine()
    {
        await using var test = new TestServiceProvider(c => c["Workflow:ApprovalMode"] = "HighValueOnly");

        // High-value (score 10 / High priority) -> must pause for review.
        var hot = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka", HotNotes);
        await SeedResearchAsync(test, hot.Website);
        var hotRun = await RunPartAAsync(test, hot.Id);

        // Routine but qualified (2 mid-intent keywords -> score 7 / Medium) -> auto-sends.
        var warm = await SeedLeadAsync(test, "Riverbend Logistics", "Dana Lee",
            "Downloaded a whitepaper last month.");
        await SeedResearchAsync(test, warm.Website);
        var warmRun = await RunPartAAsync(test, warm.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hotResult = await db.WorkflowRuns.SingleAsync(r => r.Id == hotRun);
        Assert.Equal(RunStatus.AwaitingApproval, hotResult.Status);
        Assert.Empty(await db.OutboxItems.Where(o => o.RunId == hotRun).ToListAsync());

        var warmResult = await db.WorkflowRuns.SingleAsync(r => r.Id == warmRun);
        Assert.Equal(RunStatus.Completed, warmResult.Status);
        Assert.Single(await db.OutboxItems.Where(o => o.RunId == warmRun).ToListAsync());

        var warmLead = await db.Leads.SingleAsync(l => l.Id == warm.Id);
        Assert.InRange(warmLead.Score!.Value, 5, 7);         // qualified but not high-value
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Runs Part A (qualify → research → draft → pause/auto-send) and returns the run id.</summary>
    private static async Task<Guid> RunPartAAsync(TestServiceProvider test, Guid leadId)
    {
        using var scope = test.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
        var run = await runStore.CreateAsync(leadId, CancellationToken.None);
        await workflow.RunAsync(run.Id, leadId, CancellationToken.None);
        return run.Id;
    }

    /// <summary>
    /// Mirrors what the runner does on approval (optional edit → atomic transition → resume), but
    /// synchronously so the test is deterministic.
    /// </summary>
    private static async Task ApproveAndResumeAsync(
        TestServiceProvider test, Guid runId, Guid leadId, string? subject = null, string? body = null)
    {
        using var scope = test.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();

        if (subject is not null || body is not null)
        {
            var run = await store.GetAsync(runId, CancellationToken.None);
            var pending = JsonSerializer.Deserialize<PendingApprovalState>(run!.PendingStateJson!, Json)!;
            var edited = pending.Draft with
            {
                Subject = subject ?? pending.Draft.Subject,
                Body = body ?? pending.Draft.Body
            };
            await store.UpdatePendingStateAsync(
                runId, JsonSerializer.Serialize(pending with { Draft = edited }, Json), CancellationToken.None);
        }

        Assert.True(
            await store.TryTransitionAsync(runId, RunStatus.AwaitingApproval, RunStatus.Running, CancellationToken.None),
            "expected to win the AwaitingApproval -> Running transition");
        await workflow.ResumeAfterApprovalAsync(runId, leadId, CancellationToken.None);
    }

    private static async Task<Lead> SeedLeadAsync(
        TestServiceProvider test, string company, string contact, string crmNotes)
    {
        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            CompanyName = company,
            ContactName = contact,
            ContactEmail = $"{contact.Replace(' ', '.').ToLowerInvariant()}@{company.Replace(' ', '-').ToLowerInvariant()}.example",
            Website = $"https://{company.Replace(' ', '-').ToLowerInvariant()}.example",
            Industry = "Test",
            CrmNotes = crmNotes,
            Stage = LeadStage.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }

    private static async Task SeedResearchAsync(TestServiceProvider test, string website)
    {
        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CompanyResearch.Add(new CompanyResearchEntity
        {
            Website = website,
            CompanyDescription = "A test company.",
            KnownPainPoints = new() { "manual quoting", "fragmented reporting" },
            RecentNews = new() { "Just closed a Series B." }
        });
        await db.SaveChangesAsync();
    }
}
