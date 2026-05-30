using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();
    public DbSet<OutboxItem> OutboxItems => Set<OutboxItem>();
    public DbSet<NotificationLogEntry> Notifications => Set<NotificationLogEntry>();
    internal DbSet<CompanyResearchEntity> CompanyResearch => Set<CompanyResearchEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new());

        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<Lead>(b =>
        {
            b.ToTable("Leads");
            b.HasKey(l => l.Id);
            b.Property(l => l.CompanyName).HasMaxLength(200).IsRequired();
            b.Property(l => l.ContactName).HasMaxLength(200);
            b.Property(l => l.ContactEmail).HasMaxLength(320);
            b.Property(l => l.Website).HasMaxLength(500);
            b.Property(l => l.Industry).HasMaxLength(200);
            b.Property(l => l.CrmNotes).HasMaxLength(4000);
            b.Property(l => l.ScoreReason).HasMaxLength(2000);
            b.Property(l => l.Stage).HasConversion<string>().HasMaxLength(20);
            b.Property(l => l.Priority).HasConversion<string?>().HasMaxLength(10);
            b.HasIndex(l => l.UpdatedAt);
        });

        modelBuilder.Entity<WorkflowRun>(b =>
        {
            b.ToTable("WorkflowRuns");
            b.HasKey(r => r.Id);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.ErrorMessage).HasMaxLength(4000);
            b.HasIndex(r => r.LeadId);
            b.HasIndex(r => r.StartedAt);
        });

        modelBuilder.Entity<AgentTrace>(b =>
        {
            b.ToTable("AgentTraces");
            b.HasKey(t => t.Id);
            b.Property(t => t.AgentName).HasMaxLength(100);
            b.Property(t => t.Step).HasMaxLength(100);
            b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(t => t.RunId);
            b.HasIndex(t => t.Timestamp);
        });

        modelBuilder.Entity<OutboxItem>(b =>
        {
            b.ToTable("OutboxItems");
            b.HasKey(o => o.Id);
            b.Property(o => o.ToAddress).HasMaxLength(320);
            b.Property(o => o.Subject).HasMaxLength(500);
            b.Property(o => o.FilePath).HasMaxLength(1000);
            b.HasIndex(o => o.RunId);
            b.HasIndex(o => o.GeneratedAt);
        });

        modelBuilder.Entity<NotificationLogEntry>(b =>
        {
            b.ToTable("Notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Channel).HasMaxLength(50);
            b.Property(n => n.Severity).HasConversion<string>().HasMaxLength(20);
            b.Property(n => n.Message).HasMaxLength(4000);
            b.HasIndex(n => n.Timestamp);
        });

        modelBuilder.Entity<CompanyResearchEntity>(b =>
        {
            b.ToTable("CompanyResearch");
            b.HasKey(c => c.Website);
            b.Property(c => c.Website).HasMaxLength(500);
            b.Property(c => c.CompanyDescription).HasMaxLength(2000);
            b.Property(c => c.KnownPainPoints)
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
            b.Property(c => c.RecentNews)
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
