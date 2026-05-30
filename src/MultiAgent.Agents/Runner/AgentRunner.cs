using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace MultiAgent.Agents.Runner;

public sealed class AgentRunner(ILogger<AgentRunner> logger) : IAgentRunner
{
    public async Task<TOutput> RunAsync<TOutput>(
        AIAgent agent,
        string agentName,
        string userMessage,
        Guid runId,
        CancellationToken ct) where TOutput : class
    {
        AgentResponse response;
        try
        {
            response = await agent.RunAsync(userMessage, cancellationToken: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent '{Agent}' invocation failed for run {RunId}.", agentName, runId);
            throw;
        }

        var text = response.Text ?? string.Empty;
        var json = ExtractJson(text);

        try
        {
            return JsonSerializer.Deserialize<TOutput>(json, AgentJson.Options)
                ?? throw new AgentResponseParseException(agentName, text,
                    new InvalidOperationException("Deserialized payload was null."));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Agent '{Agent}' returned non-JSON or malformed JSON. Raw (truncated): {Raw}",
                agentName, Truncate(text, 500));
            throw new AgentResponseParseException(agentName, text, ex);
        }
    }

    private static string ExtractJson(string text)
    {
        // Tolerate models that wrap JSON in ```json ... ``` fences despite instructions.
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            var endFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence > 0) text = text[..endFence];
        }
        return text.Trim();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
