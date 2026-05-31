using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using MultiAgent.Core.Abstractions;
using MultiAgent.Infrastructure.Crm;
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

        AddCrmRepository(services, configuration);
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

    /// <summary>
    /// Registers <see cref="ICrmRepository"/> based on <c>Crm:Provider</c>. Default <c>Sqlite</c>
    /// uses the in-memory/SQLite mock; <c>HubSpot</c> registers a typed <see cref="HttpClient"/>
    /// (with the bearer token + standard resilience) backing <see cref="HubSpotCrmRepository"/>.
    /// </summary>
    private static void AddCrmRepository(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrmOptions>(configuration.GetSection(CrmOptions.SectionName));
        services.Configure<HubSpotOptions>(configuration.GetSection(HubSpotOptions.SectionName));

        var crmOptions = configuration.GetSection(CrmOptions.SectionName).Get<CrmOptions>() ?? new CrmOptions();
        if (crmOptions.Provider != CrmProvider.HubSpot)
        {
            services.AddScoped<ICrmRepository, SqliteCrmRepository>();
            return;
        }

        var hubSpot = configuration.GetSection(HubSpotOptions.SectionName).Get<HubSpotOptions>() ?? new HubSpotOptions();
        if (string.IsNullOrWhiteSpace(hubSpot.AccessToken))
        {
            throw new InvalidOperationException(
                "Crm:Provider is 'HubSpot' but HubSpot:AccessToken is not configured. " +
                "Set the HubSpot__AccessToken environment variable (or user-secrets), or use Crm:Provider=Sqlite.");
        }

        services.AddSingleton<HubSpotProvisioningState>();
        services.AddHttpClient<HubSpotCrmRepository>(client =>
            {
                client.BaseAddress = new Uri(hubSpot.BaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hubSpot.AccessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // Retry GETs on transient errors; skip retrying PATCH/POST so we never duplicate a note.
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());

        services.AddScoped<ICrmRepository>(sp => sp.GetRequiredService<HubSpotCrmRepository>());
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
