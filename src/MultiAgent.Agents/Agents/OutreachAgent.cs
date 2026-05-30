using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

public sealed class OutreachAgent
{
    public const string Name = "Outreach";

    private readonly AIAgent _agent;
    private readonly IAgentRunner _runner;

    public OutreachAgent(IChatClient chatClient, IAgentRunner runner)
    {
        _runner = runner;
        _agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = PromptLoader.Load(Name),
                Temperature = 0.7f,
                MaxOutputTokens = 700,
                ResponseFormat = ChatResponseFormat.Json
            }
        });
    }

    public Task<OutreachDraft> InvokeAsync(
        Lead lead,
        LeadScore score,
        ResearchSummary research,
        Guid runId,
        CancellationToken ct)
    {
        var input = new
        {
            Lead = lead,
            Score = score,
            Research = research
        };
        var userMessage = JsonSerializer.Serialize(input, AgentJson.Options);
        return _runner.RunAsync<OutreachDraft>(_agent, Name, userMessage, runId, ct);
    }
}
