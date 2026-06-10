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

        // Human-in-the-loop: approve a run paused at AwaitingApproval (optionally editing the draft's
        // subject/body) and resume it, or reject it so no email is sent. 409 if it is not awaiting.
        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            ApproveRequest? body,
            IWorkflowRunner runner,
            CancellationToken ct) =>
        {
            var run = await runner.GetRunAsync(id, ct);
            if (run is null) return Results.NotFound();
            var approved = await runner.ApproveAsync(id, body?.Subject, body?.Body, ct);
            return approved
                ? Results.Accepted($"/api/runs/{id}", new { runId = id })
                : Results.Conflict(new { error = "Run is not awaiting approval." });
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            RejectRequest? body,
            IWorkflowRunner runner,
            CancellationToken ct) =>
        {
            var run = await runner.GetRunAsync(id, ct);
            if (run is null) return Results.NotFound();
            var rejected = await runner.RejectAsync(id, body?.Reason, ct);
            return rejected
                ? Results.Ok(new { runId = id })
                : Results.Conflict(new { error = "Run is not awaiting approval." });
        });

        return app;
    }

    public sealed record ApproveRequest(string? Subject, string? Body);
    public sealed record RejectRequest(string? Reason);
}
