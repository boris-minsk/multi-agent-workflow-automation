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

                if (string.IsNullOrWhiteSpace(openAi.ApiKey))
                {
                    throw new InvalidOperationException(
                        "Llm:Provider is 'OpenAI' but OpenAI:ApiKey is not configured. " +
                        "Set the OpenAI__ApiKey environment variable or fall back to Llm:Provider=Mock.");
                }

                var underlying = new OpenAIClient(openAi.ApiKey)
                    .GetChatClient(llm.Model)
                    .AsIChatClient();

                return new ChatClientBuilder(underlying)
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
