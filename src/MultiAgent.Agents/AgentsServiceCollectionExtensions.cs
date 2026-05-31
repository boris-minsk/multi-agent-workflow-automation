using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MultiAgent.Agents.Agents;
using MultiAgent.Agents.Runner;
using MultiAgent.Agents.Workflow;
using MultiAgent.Core.Abstractions;

namespace MultiAgent.Agents;

public static class AgentsServiceCollectionExtensions
{
    public static IServiceCollection AddAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.SectionName));

        // CRM tool-calling is OpenAI-only (the mock cannot call tools). Gate on the raw config
        // string so MultiAgent.Agents stays free of the Infrastructure LlmOptions type.
        var crmToolsEnabled = string.Equals(
            configuration["Llm:Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase);
        services.AddSingleton(new CrmToolPolicy(crmToolsEnabled));

        // Runner: inner runner + tracing/retry decorator. Both must see the same DI scope-less
        // dependencies; both are safe as singletons.
        services.AddSingleton<AgentRunner>();
        services.AddSingleton<IAgentRunner>(sp =>
        {
            var inner = sp.GetRequiredService<AgentRunner>();
            return new TracingAgentRunner(
                inner,
                sp.GetRequiredService<IAgentTracer>(),
                sp.GetRequiredService<INotificationSink>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TracingAgentRunner>>());
        });

        // Agents — each holds a constructed AIAgent and delegates to IAgentRunner.
        // Safe as singletons: ChatClientAgent is reusable and stateless across calls.
        services.AddSingleton<LeadQualificationAgent>();
        services.AddSingleton<ResearchAgent>();
        services.AddSingleton<OutreachAgent>();
        services.AddSingleton<CrmUpdateAgent>();

        // Workflow — scoped because it uses scoped repositories (DbContext-backed).
        services.AddScoped<SalesFollowUpWorkflow>();

        // Public workflow entry-point.
        services.AddSingleton<IWorkflowRunner, WorkflowRunner>();

        return services;
    }
}
