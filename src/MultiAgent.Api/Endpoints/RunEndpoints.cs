using MultiAgent.Core.Abstractions;

namespace MultiAgent.Api.Endpoints;

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/runs").WithTags("Runs");

        group.MapGet("/", async (IWorkflowRunner runner, int? take, CancellationToken ct) =>
            Results.Ok(await runner.ListRunsAsync(take ?? 50, ct)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            IWorkflowRunner runner,
            IAgentTracer tracer,
            CancellationToken ct) =>
        {
            var run = await runner.GetRunAsync(id, ct);
            if (run is null) return Results.NotFound();
            var traces = await tracer.GetByRunAsync(id, ct);
            return Results.Ok(new { run, traces });
        });

        return app;
    }
}
