using MultiAgent.Core.Abstractions;

namespace MultiAgent.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("/", async (INotificationSink sink, int? take, CancellationToken ct) =>
            Results.Ok(await sink.ListAsync(take ?? 100, ct)));

        return app;
    }
}
