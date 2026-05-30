using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

public sealed class CrmUpdateAgent
{
    public const string Name = "CrmUpdate";

    private readonly AIAgent _agent;
    private readonly IAgentRunner _runner;

    public CrmUpdateAgent(IChatClient chatClient, IAgentRunner runner)
    {
        _runner = runner;
        _agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = PromptLoader.Load(Name),
                Temperature = 0.2f,
                MaxOutputTokens = 800,
                ResponseFormat = ChatResponseFormat.Json
            }
        });
    }

    public Task<CrmUpdateInstructions> InvokeAsync(
        Lead lead,
        LeadScore score,
        ResearchSummary? research,
        OutreachDraft? draft,
        bool emailSent,
        Guid runId,
        CancellationToken ct)
    {
        var input = new
        {
            Lead = lead,
            Score = score,
            Research = research,
            Draft = draft,
            EmailSent = emailSent
        };
        var userMessage = JsonSerializer.Serialize(input, AgentJson.Options);
        return _runner.RunAsync<CrmUpdateInstructions>(_agent, Name, userMessage, runId, ct);
    }
}
