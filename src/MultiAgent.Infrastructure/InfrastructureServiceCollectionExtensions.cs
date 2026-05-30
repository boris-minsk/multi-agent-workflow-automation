using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MultiAgent.Core.Abstractions;
using MultiAgent.Infrastructure.Email;
using MultiAgent.Infrastructure.Notes;
using MultiAgent.Infrastructure.Notifications;
using MultiAgent.Infrastructure.Options;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQLite persistence, mock external adapters (file email, console notifications,
    /// markdown notes), and the database initializer that runs migrations + seeding on startup.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool addDatabaseInitializer = true)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));

        var paths = configuration.GetSection(PathsOptions.SectionName).Get<PathsOptions>()
            ?? new PathsOptions();

        // Resolve relative paths against AppContext.BaseDirectory so the app finds files
        // whether launched via `dotnet run` (project dir) or `dotnet App.dll` (output dir).
        paths.SqliteDb = ResolvePath(paths.SqliteDb);
        paths.SeedDataFile = ResolvePath(paths.SeedDataFile);
        paths.SeedResearchFile = ResolvePath(paths.SeedResearchFile);
        paths.OutboxDirectory = ResolvePath(paths.OutboxDirectory);
        paths.NotesDirectory = ResolvePath(paths.NotesDirectory);

        services.PostConfigure<PathsOptions>(opts =>
        {
            opts.SqliteDb = paths.SqliteDb;
            opts.SeedDataFile = paths.SeedDataFile;
            opts.SeedResearchFile = paths.SeedResearchFile;
            opts.OutboxDirectory = paths.OutboxDirectory;
            opts.NotesDirectory = paths.NotesDirectory;
        });

        EnsureDirectoryExistsForFile(paths.SqliteDb);
        Directory.CreateDirectory(paths.OutboxDirectory);
        Directory.CreateDirectory(paths.NotesDirectory);

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={paths.SqliteDb}"));

        services.AddScoped<ICrmRepository, SqliteCrmRepository>();
        services.AddScoped<ICompanyResearchSource, SqliteCompanyResearchSource>();
        services.AddScoped<IEmailSender, FileSystemEmailSender>();
        services.AddScoped<IWorkflowRunStore, SqliteWorkflowRunStore>();
        services.AddSingleton<IAgentTracer, SqliteAgentTracer>();
        services.AddSingleton<INotificationSink, ConsoleNotificationSink>();
        services.AddSingleton<INotesStore, MarkdownNotesStore>();

        services.AddScoped<SeedLoader>();
        if (addDatabaseInitializer)
        {
            services.AddHostedService<DatabaseInitializer>();
        }

        return services;
    }

    private static void EnsureDirectoryExistsForFile(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(path, AppContext.BaseDirectory);
}
