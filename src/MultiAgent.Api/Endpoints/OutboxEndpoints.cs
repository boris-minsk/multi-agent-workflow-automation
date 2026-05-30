using MultiAgent.Core.Abstractions;

namespace MultiAgent.Api.Endpoints;

public static class OutboxEndpoints
{
    public static IEndpointRouteBuilder MapOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/outbox").WithTags("Outbox");

        group.MapGet("/", async (IEmailSender sender, int? take, CancellationToken ct) =>
            Results.Ok(await sender.ListAsync(take ?? 50, ct)));

        group.MapGet("/{id:guid}", async (Guid id, IEmailSender sender, CancellationToken ct) =>
        {
            var item = await sender.GetAsync(id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        return app;
    }
}
