using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

public sealed class LeadQualificationAgent
{
    public const string Name = "LeadQualifier";

    private readonly AIAgent _agent;
    private readonly IAgentRunner _runner;

    public LeadQualificationAgent(IChatClient chatClient, IAgentRunner runner)
    {
        _runner = runner;
        _agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = PromptLoader.Load(Name),
                Temperature = 0.1f,
                MaxOutputTokens = 600,
                ResponseFormat = ChatResponseFormat.Json
            }
        });
    }

    public Task<LeadScore> InvokeAsync(Lead lead, Guid runId, CancellationToken ct)
    {
        var userMessage = JsonSerializer.Serialize(lead, AgentJson.Options);
        return _runner.RunAsync<LeadScore>(_agent, Name, userMessage, runId, ct);
    }
}
