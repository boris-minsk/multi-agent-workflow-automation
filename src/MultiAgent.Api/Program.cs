using MultiAgent.Agents;
using MultiAgent.Api.Endpoints;
using MultiAgent.Infrastructure;
using MultiAgent.Infrastructure.Llm;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day));

    builder.Services.AddOpenApi();

    builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
    {
        jsonOptions.SerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddLlm(builder.Configuration);
    builder.Services.AddAgents(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapLeadEndpoints();
    app.MapRunEndpoints();
    app.MapOutboxEndpoints();
    app.MapNotificationEndpoints();
    app.MapHealthEndpoints();

    Log.Information("MultiAgent.Api starting...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MultiAgent.Api host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
