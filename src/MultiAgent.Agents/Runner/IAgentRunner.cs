using Microsoft.Agents.AI;

namespace MultiAgent.Agents.Runner;

/// <summary>
/// Executes an <see cref="AIAgent"/>, parses its JSON response into <typeparamref name="TOutput"/>,
/// and is the single integration point for cross-cutting concerns (retry, tracing, notifications).
/// </summary>
public interface IAgentRunner
{
    Task<TOutput> RunAsync<TOutput>(
        AIAgent agent,
        string agentName,
        string userMessage,
        Guid runId,
        CancellationToken ct) where TOutput : class;
}
