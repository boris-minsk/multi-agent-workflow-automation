using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Infrastructure.Llm;

/// <summary>
/// An <see cref="IChatClient"/> implementation that returns canned JSON responses
/// keyed by the agent identity it sees in the system prompt. Used for offline
/// development, tests, and the no-API-key demo path.
/// </summary>
public sealed class MockChatClient(
    IOptions<LlmOptions> llmOptions,
    ILogger<MockChatClient> logger) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        var systemText = string.Join("\n", new[] { options?.Instructions ?? string.Empty }
            .Concat(messageList.Where(m => m.Role == ChatRole.System).Select(m => m.Text)));
        var userText = string.Join("\n",
            messageList.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        var agentKey = DetermineAgentKey(systemText);

        var throwOnAgent = llmOptions.Value.MockThrowOnAgent;
        if (!string.IsNullOrEmpty(throwOnAgent) &&
            throwOnAgent.Equals(agentKey, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("MockChatClient throwing for agent '{Agent}' per MockThrowOnAgent config.", agentKey);
            throw new InvalidOperationException($"MockChatClient configured to throw for agent '{agentKey}'.");
        }

        var json = agentKey switch
        {
            "LeadQualifier" => MockResponseGenerator.GenerateLeadScore(userText),
            "Research" => MockResponseGenerator.GenerateResearchSummary(userText),
            "Outreach" => MockResponseGenerator.GenerateOutreachDraft(userText),
            "CrmUpdate" => MockResponseGenerator.GenerateCrmUpdate(userText),
            _ => "{\"info\":\"mock fallback — system prompt did not match any known agent\"}"
        };

        logger.LogDebug("MockChatClient returning canned response for agent '{Agent}' ({Bytes} chars).",
            agentKey, json.Length);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            ModelId = "mock",
            FinishReason = ChatFinishReason.Stop
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var msg in response.Messages)
        {
            yield return new ChatResponseUpdate(msg.Role, msg.Text);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(MockChatClient) ? this : null;

    public void Dispose() { }

    private static string DetermineAgentKey(string systemText)
    {
        // Match each prompt's distinctive opening role phrase so substrings in shared context
        // (e.g. "research summary" in the Outreach prompt) don't misroute.
        var lower = systemText.ToLowerInvariant();
        if (lower.Contains("b2b sales lead qualifier")) return "LeadQualifier";
        if (lower.Contains("crm update agent")) return "CrmUpdate";
        if (lower.Contains("outreach agent")) return "Outreach";
        if (lower.Contains("research agent")) return "Research";
        return "Unknown";
    }
}
