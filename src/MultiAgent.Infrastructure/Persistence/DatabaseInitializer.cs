using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiAgent.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF migrations and seeds initial data on app startup.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<SeedLoader>();

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Seeding data if needed...");
        await seeder.SeedIfEmptyAsync(cancellationToken);

        logger.LogInformation("Database initialization complete.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
