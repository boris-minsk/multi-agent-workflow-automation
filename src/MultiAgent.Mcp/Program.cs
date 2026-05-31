using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MultiAgent.Infrastructure;
using MultiAgent.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

// stdio transport uses stdout for the JSON-RPC channel, so ALL logging must go to stderr —
// anything on stdout that isn't JSON-RPC corrupts the protocol.
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Reuse the app's infrastructure DI so MCP tools hit the same CRM the API uses, honoring
// Crm:Provider (Sqlite mock or real HubSpot). Skip the hosted DB initializer — this is a
// short-lived stdio process, not the web host.
builder.Services.AddInfrastructure(builder.Configuration, addDatabaseInitializer: false);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// In SQLite (mock) mode, ensure the schema exists so the tools work standalone. HubSpot mode
// needs no local database. Point Paths:SqliteDb at the API's db to share its seeded leads.
if (!string.Equals(builder.Configuration["Crm:Provider"], "HubSpot", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
