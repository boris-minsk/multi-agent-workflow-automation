using Microsoft.Extensions.Options;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (IOptions<LlmOptions> llmOptions) => Results.Ok(new
        {
            status = "ok",
            llmProvider = llmOptions.Value.Provider.ToString(),
            llmModel = llmOptions.Value.Model,
            time = DateTime.UtcNow
        })).WithTags("Health");

        return app;
    }
}
