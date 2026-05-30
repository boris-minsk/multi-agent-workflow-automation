using MultiAgent.Core.Abstractions;

namespace MultiAgent.Api.Endpoints;

public static class LeadEndpoints
{
    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/leads").WithTags("Leads");

        group.MapGet("/", async (ICrmRepository crm, CancellationToken ct) =>
            Results.Ok(await crm.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, ICrmRepository crm, CancellationToken ct) =>
        {
            var lead = await crm.GetAsync(id, ct);
            return lead is null ? Results.NotFound() : Results.Ok(lead);
        });

        group.MapPost("/{id:guid}/run", async (Guid id, IWorkflowRunner runner, ICrmRepository crm, CancellationToken ct) =>
        {
            var lead = await crm.GetAsync(id, ct);
            if (lead is null) return Results.NotFound(new { error = $"Lead {id} not found" });
            var runId = await runner.StartAsync(id, ct);
            return Results.Accepted($"/api/runs/{runId}", new { runId });
        });

        return app;
    }
}
