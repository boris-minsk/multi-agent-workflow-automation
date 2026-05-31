using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Infrastructure.Options;
using OpenAI;

namespace MultiAgent.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IChatClient"/> based on <c>Llm:Provider</c> configuration.
    /// Provider <c>Mock</c> registers <see cref="MockChatClient"/>; provider <c>OpenAI</c>
    /// registers a real OpenAI-backed client using <c>OpenAI:ApiKey</c>.
    /// </summary>
    public static IServiceCollection AddLlm(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));

        var llmOptions = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>()
            ?? new LlmOptions();

        if (llmOptions.Provider == LlmProvider.OpenAI)
        {
            services.AddSingleton<IChatClient>(sp =>
            {
                var openAi = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                // Accept either OpenAI:ApiKey (env: OpenAI__ApiKey) or the OpenAI SDK's standard
                // OPENAI_API_KEY env var, so a key set the conventional way just works.
                var apiKey = !string.IsNullOrWhiteSpace(openAi.ApiKey)
                    ? openAi.ApiKey
                    : Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        "Llm:Provider is 'OpenAI' but no API key is configured. Set OpenAI__ApiKey " +
                        "(config) or the standard OPENAI_API_KEY environment variable, or use Llm:Provider=Mock.");
                }

                var underlying = new OpenAIClient(apiKey)
                    .GetChatClient(llm.Model)
                    .AsIChatClient();

                // UseFunctionInvocation is outermost so it can intercept tool calls and loop
                // (re-invoking the inner client). It's a no-op for tool-less requests, so the
                // qualifier/research/outreach agents are unaffected; only CrmUpdate ships tools.
                return new ChatClientBuilder(underlying)
                    .UseFunctionInvocation(loggerFactory)
                    .UseLogging(loggerFactory)
                    .Build();
            });
        }
        else
        {
            services.AddSingleton<IChatClient, MockChatClient>();
        }

        return services;
    }
}
