using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiAgent.Agents.Workflow;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Llm;
using MultiAgent.Infrastructure.Options;
using MultiAgent.Infrastructure.Persistence;
using MultiAgent.Tests.TestFixtures;

namespace MultiAgent.Tests.Workflow;

public class SalesFollowUpWorkflowTests
{
    [Fact]
    public async Task HighIntent_Run_CompletesAndSendsEmail()
    {
        await using var test = new TestServiceProvider();
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka",
            "VP booked a demo. Procurement said budget is approved. Timeline 30 days.");
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunWorkflowAsync(test, lead.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Null(run.ErrorMessage);

        var updatedLead = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.True(updatedLead.Score >= 5,
            $"expected score >= 5 for high-intent lead, got {updatedLead.Score}");
        Assert.Equal(LeadStage.Contacted, updatedLead.Stage);

        var outbox = await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync();
        var email = Assert.Single(outbox);
        Assert.False(string.IsNullOrWhiteSpace(email.Subject), "subject should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(email.Body), "body should not be empty");

        var notifications = await db.Notifications.Where(n => n.RunId == runId).ToListAsync();
        Assert.NotEmpty(notifications);

        Assert.True(File.Exists(email.FilePath), $".eml file should exist at {email.FilePath}");
    }

    [Fact]
    public async Task LowIntent_Run_IsSkippedAndNoEmail()
    {
        await using var test = new TestServiceProvider();
        var lead = await SeedLeadAsync(test, "Sleepy Bakery", "Tom Reyes",
            "Free-tier user with one seat. No email engagement. Small shop.");

        var runId = await RunWorkflowAsync(test, lead.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Skipped, run.Status);

        var updatedLead = await db.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.True(updatedLead.Score <= 4,
            $"expected score <= 4 for low-intent lead, got {updatedLead.Score}");
        Assert.Equal(LeadStage.Disqualified, updatedLead.Stage);

        var outbox = await db.OutboxItems.Where(o => o.RunId == runId).ToListAsync();
        Assert.Empty(outbox);
    }

    [Fact]
    public async Task AgentFailure_RetriesThenFailsTerminally()
    {
        await using var test = new TestServiceProvider(c =>
        {
            c["Llm:MockThrowOnAgent"] = "Outreach";
        });
        var lead = await SeedLeadAsync(test, "Acme Robotics", "Yuki Tanaka",
            "VP booked a demo. Budget approved.");
        await SeedResearchAsync(test, lead.Website);

        var runId = await RunWorkflowAsync(test, lead.Id);

        using var scope = test.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.WorkflowRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);

        var traces = await db.AgentTraces
            .Where(t => t.RunId == runId && t.AgentName == "Outreach")
            .ToListAsync();
        // MaxRetries=2 → initial + 2 retries = 3 attempts, all failed
        Assert.Equal(3, traces.Count);
        Assert.All(traces, t => Assert.Equal(RunStatus.Failed, t.Status));

        // The Monitoring/Recovery role posts an error notification.
        var errorNotifications = await db.Notifications
            .Where(n => n.RunId == runId && n.Severity == NotificationSeverity.Error)
            .ToListAsync();
        Assert.NotEmpty(errorNotifications);
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

    private static async Task<Guid> RunWorkflowAsync(TestServiceProvider test, Guid leadId)
    {
        using var scope = test.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var workflow = scope.ServiceProvider.GetRequiredService<SalesFollowUpWorkflow>();
        var run = await runStore.CreateAsync(leadId, CancellationToken.None);
        await workflow.RunAsync(run.Id, leadId, CancellationToken.None);
        return run.Id;
    }
}
