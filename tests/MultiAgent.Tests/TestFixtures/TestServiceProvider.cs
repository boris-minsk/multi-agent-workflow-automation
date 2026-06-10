using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Agents;
using MultiAgent.Infrastructure;
using MultiAgent.Infrastructure.Llm;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Tests.TestFixtures;

/// <summary>
/// Builds an isolated DI container for a single test: mock LLM, temp SQLite DB, no
/// background hosted services. The temp directory is cleaned up on disposal.
/// </summary>
public sealed class TestServiceProvider : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    public string TempDir { get; }

    public TestServiceProvider(Action<Dictionary<string, string?>>? configOverride = null)
    {
        TempDir = Path.Combine(Path.GetTempPath(), "multiagent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);

        var configValues = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = "Mock",
            ["Llm:Model"] = "mock",
            ["Paths:SqliteDb"] = Path.Combine(TempDir, "test.db"),
            ["Paths:OutboxDirectory"] = Path.Combine(TempDir, "outbox"),
            ["Paths:NotesDirectory"] = Path.Combine(TempDir, "notes"),
            ["Paths:SeedDataFile"] = Path.Combine(TempDir, "no-seed.json"),
            ["Paths:SeedResearchFile"] = Path.Combine(TempDir, "no-research.json"),
            ["Workflow:QualificationThreshold"] = "5",
            ["Workflow:MaxRetries"] = "2",
            ["Workflow:RetryBaseDelayMs"] = "10",
            // HITL (Human-In-The-Loop) approval is OFF by default in tests so the existing suite is unaffected; the
            // approval tests opt in via the configOverride below.
            ["Workflow:ApprovalMode"] = "Never"
        };
        configOverride?.Invoke(configValues);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(config, addDatabaseInitializer: false);
        services.AddLlm(config);
        services.AddAgents(config);

        _provider = services.BuildServiceProvider();

        // Run migrations synchronously for the test DB.
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }

    public IServiceProvider Services => _provider;

    public IServiceScope CreateScope() => _provider.CreateScope();

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        try { Directory.Delete(TempDir, recursive: true); } catch { /* best-effort */ }
    }
}
