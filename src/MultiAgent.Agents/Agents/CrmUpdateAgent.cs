using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

public sealed class CrmUpdateAgent
{
    public const string Name = "CrmUpdate";

    private readonly IChatClient _chatClient;
    private readonly IAgentRunner _runner;
    private readonly CrmToolPolicy _toolPolicy;
    private readonly ILogger<CrmUpdateAgent> _logger;
    private readonly string _instructions;
    private readonly AIAgent _baseAgent;

    public CrmUpdateAgent(IChatClient chatClient, IAgentRunner runner, CrmToolPolicy toolPolicy, ILogger<CrmUpdateAgent> logger)
    {
        _chatClient = chatClient;
        _runner = runner;
        _toolPolicy = toolPolicy;
        _logger = logger;
        _instructions = PromptLoader.Load(Name);

        // No-tools agent for mock mode (and any non-tool provider). Built once; reusable.
        _baseAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = _instructions,
                Temperature = 0.2f,
                MaxOutputTokens = 800,
                ResponseFormat = ChatResponseFormat.Json
            }
        });
    }

    /// <summary>
    /// Produces CRM update instructions. In tool mode (<c>Llm:Provider=OpenAI</c>) the agent
    /// additionally calls <c>update_lead_stage</c>/<c>add_crm_note</c> against the current lead;
    /// the returned <see cref="CrmUpdateResult"/> reports which writes the tools performed so the
    /// workflow can avoid double-writing while keeping a deterministic fallback. <paramref name="crm"/>
    /// is supplied by the (scoped) caller because this agent is registered as a singleton.
    /// </summary>
    public async Task<CrmUpdateResult> InvokeAsync(
        Lead lead,
        LeadScore score,
        ResearchSummary? research,
        OutreachDraft? draft,
        bool emailSent,
        Guid runId,
        ICrmRepository crm,
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

        if (!_toolPolicy.Enabled)
        {
            var instructions = await _runner.RunAsync<CrmUpdateInstructions>(_baseAgent, Name, userMessage, runId, ct);
            return new CrmUpdateResult(instructions, StageWrittenByTool: false, NoteWrittenByTool: false);
        }

        // Tool mode: bind write tools to this lead and let the model call them. The JSON
        // response-format is intentionally omitted here — OpenAI rejects a forced json_object on
        // the same turn as tool calls. The prompt still requires a JSON object (for the run
        // summary + fallback) and AgentRunner tolerates fences/prose.
        var tools = new CrmAgentTools(crm, lead.Id, _logger);
        var toolAgent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = Name,
            ChatOptions = new ChatOptions
            {
                Instructions = _instructions,
                Temperature = 0.2f,
                MaxOutputTokens = 800,
                Tools = tools.AsTools()
            }
        });

        var toolInstructions = await _runner.RunAsync<CrmUpdateInstructions>(toolAgent, Name, userMessage, runId, ct);
        return new CrmUpdateResult(toolInstructions, tools.StageWritten, tools.NoteWritten);
    }
}
