using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using MultiAgent.Core.Abstractions;
using MultiAgent.Infrastructure;
using MultiAgent.Infrastructure.Email;
using MultiAgent.Infrastructure.Options;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Tests.Email;

public sealed class SmtpEmailSenderTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task SendAsync_Sends_PersistsRow_AndWritesEmlAudit()
    {
        var tempDir = NewTempDir();
        await using var db = NewDb(tempDir);
        var dispatcher = new RecordingSmtpDispatcher();
        var sender = NewSender(dispatcher, db, tempDir);

        var runId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var item = await sender.SendAsync(runId, leadId, "lead@acme.com", "Quick question", "Hello there.", CancellationToken.None);

        // The message handed to the SMTP boundary carries the drafted fields + the configured sender.
        Assert.NotNull(dispatcher.LastMessage);
        var sent = dispatcher.LastMessage!;
        Assert.Equal("Quick question", sent.Subject);
        Assert.Equal("lead@acme.com", sent.To.Mailboxes.Single().Address);
        Assert.Equal("sales@myco.com", sent.From.Mailboxes.Single().Address);
        Assert.Equal("Sales Bot", sent.From.Mailboxes.Single().Name);
        Assert.Equal("Hello there.", sent.TextBody);
        Assert.Equal(runId.ToString(), sent.Headers["X-MultiAgent-RunId"]);

        // The outbox row mirrors the file-mock shape so the dashboard is provider-agnostic.
        Assert.Equal(runId, item.RunId);
        Assert.Equal(leadId, item.LeadId);
        Assert.Equal("lead@acme.com", item.ToAddress);
        Assert.True(File.Exists(item.FilePath));
        Assert.Contains("Quick question", await File.ReadAllTextAsync(item.FilePath));

        // …and it is readable back through the abstraction.
        var roundTrip = await sender.GetAsync(item.Id, CancellationToken.None);
        Assert.NotNull(roundTrip);
        Assert.Equal(item.Id, roundTrip!.Id);
    }

    [Fact]
    public async Task SendAsync_WhenTransportFails_PersistsNothing()
    {
        var tempDir = NewTempDir();
        await using var db = NewDb(tempDir);
        var dispatcher = new RecordingSmtpDispatcher { ThrowOnSend = true };
        var sender = NewSender(dispatcher, db, tempDir);

        var runId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(runId, Guid.NewGuid(), "lead@acme.com", "Subj", "Body", CancellationToken.None));

        // Send-first semantics: a transport failure leaves no row and no .eml behind.
        Assert.Empty(await db.OutboxItems.ToListAsync());
        Assert.False(File.Exists(Path.Combine(tempDir, "outbox", $"{runId:N}.eml")));
    }

    [Fact]
    public void Di_DefaultProvider_RegistersFileSender()
    {
        using var sp = BuildProvider(NewTempDir());
        using var scope = sp.CreateScope();
        Assert.IsType<FileSystemEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Di_SmtpProviderWithCredentials_RegistersSmtpSender()
    {
        using var sp = BuildProvider(NewTempDir(), new()
        {
            ["Email:Provider"] = "Smtp",
            ["Smtp:Username"] = "sales@myco.com",
            ["Smtp:Password"] = "app-password"
        });
        using var scope = sp.CreateScope();
        Assert.IsType<SmtpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Di_SmtpProviderWithoutCredentials_Throws()
    {
        var config = BuildConfig(NewTempDir(), new() { ["Email:Provider"] = "Smtp" });
        var services = new ServiceCollection().AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(config, addDatabaseInitializer: false));
        Assert.Contains("Smtp:Username", ex.Message);
    }

    // --- helpers ---

    private static SmtpEmailSender NewSender(ISmtpDispatcher dispatcher, AppDbContext db, string tempDir) =>
        new(
            dispatcher,
            db,
            Options.Create(new SmtpOptions { FromAddress = "sales@myco.com", FromName = "Sales Bot" }),
            Options.Create(new PathsOptions { OutboxDirectory = Path.Combine(tempDir, "outbox") }),
            NullLogger<SmtpEmailSender>.Instance);

    private static AppDbContext NewDb(string tempDir)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(tempDir, "test.db")}")
            .Options;
        var db = new AppDbContext(options);
        db.Database.Migrate();
        return db;
    }

    private static ServiceProvider BuildProvider(string tempDir, Dictionary<string, string?>? overrides = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(BuildConfig(tempDir, overrides), addDatabaseInitializer: false);
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(string tempDir, Dictionary<string, string?>? overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Paths:SqliteDb"] = Path.Combine(tempDir, "test.db"),
            ["Paths:OutboxDirectory"] = Path.Combine(tempDir, "outbox"),
            ["Paths:NotesDirectory"] = Path.Combine(tempDir, "notes"),
            ["Paths:SeedDataFile"] = Path.Combine(tempDir, "no-seed.json"),
            ["Paths:SeedResearchFile"] = Path.Combine(tempDir, "no-research.json")
        };
        if (overrides is not null)
        {
            foreach (var (k, v) in overrides)
            {
                values[k] = v;
            }
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "multiagent-email-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class RecordingSmtpDispatcher : ISmtpDispatcher
    {
        public MimeMessage? LastMessage { get; private set; }
        public bool ThrowOnSend { get; init; }

        public Task SendAsync(MimeMessage message, CancellationToken ct)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("simulated SMTP failure");
            }
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
