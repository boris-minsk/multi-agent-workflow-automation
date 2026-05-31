using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MultiAgent.Agents;
using MultiAgent.Agents.Agents;
using MultiAgent.Agents.Runner;
using MultiAgent.Core.Models;
using MultiAgent.Tests.TestFixtures;

namespace MultiAgent.Tests.Agents;

public class CrmUpdateAgentToolTests
{
    [Fact]
    public async Task ToolMode_AgentCallsUpdateStageTool_AndReportsApplied()
    {
        var crm = new RecordingCrmRepository();
        // Wrap the scripted client exactly like the OpenAI path does, so tool calls auto-invoke.
        IChatClient client = new ChatClientBuilder(new ScriptedToolChatClient()).UseFunctionInvocation().Build();
        var agent = new CrmUpdateAgent(client, new AgentRunner(NullLogger<AgentRunner>.Instance),
            new CrmToolPolicy(enabled: true), NullLogger<CrmUpdateAgent>.Instance);

        var lead = new Lead { Id = Guid.NewGuid(), CompanyName = "Acme", ContactEmail = "a@acme.com" };
        var score = new LeadScore { Score = 8, Priority = Priority.High, Reason = "strong intent" };

        var result = await agent.InvokeAsync(lead, score, research: null, draft: null,
            emailSent: true, runId: Guid.NewGuid(), crm, CancellationToken.None);

        Assert.True(result.StageWrittenByTool);              // the agent moved the stage via the tool
        Assert.False(result.NoteWrittenByTool);              // it did not call add_crm_note
        var stage = Assert.Single(crm.StageUpdates);
        Assert.Equal(lead.Id, stage.Id);
        Assert.Equal(LeadStage.Contacted, stage.Stage);
        Assert.Equal(LeadStage.Contacted, result.Instructions.TargetStage); // final JSON still parsed
    }

    [Fact]
    public async Task NonToolMode_AgentDoesNotTouchCrm_AndReturnsInstructions()
    {
        var crm = new RecordingCrmRepository();
        IChatClient client = new StaticJsonChatClient(
            """{"TargetStage":"Disqualified","ShortNote":"low score","MarkdownNote":"# Summary"}""");
        var agent = new CrmUpdateAgent(client, new AgentRunner(NullLogger<AgentRunner>.Instance),
            new CrmToolPolicy(enabled: false), NullLogger<CrmUpdateAgent>.Instance);

        var lead = new Lead { Id = Guid.NewGuid() };
        var score = new LeadScore { Score = 2, Priority = Priority.Low, Reason = "cold" };

        var result = await agent.InvokeAsync(lead, score, research: null, draft: null,
            emailSent: false, runId: Guid.NewGuid(), crm, CancellationToken.None);

        Assert.False(result.StageWrittenByTool);
        Assert.False(result.NoteWrittenByTool);
        Assert.Empty(crm.StageUpdates);   // in non-tool mode the agent never writes; the workflow applies
        Assert.Empty(crm.Notes);
        Assert.Equal(LeadStage.Disqualified, result.Instructions.TargetStage);
    }

    /// <summary>Emits one <c>update_lead_stage</c> tool call, then a final CrmUpdateInstructions JSON.</summary>
    private sealed class ScriptedToolChatClient : IChatClient
    {
        private int _turn;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (_turn++ == 0)
            {
                var call = new FunctionCallContent("call-1", "update_lead_stage",
                    new Dictionary<string, object?> { ["stage"] = "Contacted" });
                var toolMessage = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
                return Task.FromResult(new ChatResponse(toolMessage) { FinishReason = ChatFinishReason.ToolCalls });
            }

            const string finalJson =
                """{"TargetStage":"Contacted","ShortNote":"Outreach sent. Awaiting reply.","MarkdownNote":"# Summary"}""";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, finalJson))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Returns a fixed JSON string regardless of input (no tools).</summary>
    private sealed class StaticJsonChatClient(string json) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                FinishReason = ChatFinishReason.Stop
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
