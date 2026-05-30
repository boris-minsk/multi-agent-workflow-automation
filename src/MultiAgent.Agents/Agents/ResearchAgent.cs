using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

public sealed class ResearchAgent
{
    public const string Name = "Research";

    private readonly AIAgent _agent;
    private readonly IAgentRunner _runner;

    public ResearchAgent(IChatClient chatClient, IAgentRunner runner)
    {
        _runner = runner;
        _agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = PromptLoader.Load(Name),
                Temperature = 0.3f,
                MaxOutputTokens = 1000,
                ResponseFormat = ChatResponseFormat.Json
            }
        });
    }

    public Task<ResearchSummary> InvokeAsync(
        Lead lead,
        CompanyInfo? companyInfo,
        string crmHistory,
        Guid runId,
        CancellationToken ct)
    {
        var input = new
        {
            Lead = lead,
            CompanyInfo = companyInfo,
            CrmHistory = crmHistory
        };
        var userMessage = JsonSerializer.Serialize(input, AgentJson.Options);
        return _runner.RunAsync<ResearchSummary>(_agent, Name, userMessage, runId, ct);
    }
}
