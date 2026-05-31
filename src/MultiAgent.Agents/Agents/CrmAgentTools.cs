using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

/// <summary>
/// Per-run CRM write tools bound to a single lead, exposed to the CrmUpdate agent in tool mode.
/// Built per invocation because it carries the lead id and the caller's <em>scoped</em>
/// <see cref="ICrmRepository"/> (the agent itself is a singleton). Each call records that it
/// fired (so the workflow can skip the corresponding deterministic write) and logs it.
/// </summary>
internal sealed class CrmAgentTools(ICrmRepository crm, Guid leadId, ILogger logger)
{
    public bool StageWritten { get; private set; }
    public bool NoteWritten { get; private set; }

    public async Task<string> UpdateLeadStage(
        [Description("Target pipeline stage: New, Qualified, Contacted, Replied, or Disqualified")] string stage,
        CancellationToken ct)
    {
        if (!Enum.TryParse<LeadStage>(stage, ignoreCase: true, out var parsed))
        {
            return $"'{stage}' is not a valid stage. Valid values: New, Qualified, Contacted, Replied, Disqualified.";
        }

        await crm.UpdateStageAsync(leadId, parsed, ct);
        StageWritten = true;
        logger.LogInformation(
            "CRM tool 'update_lead_stage' invoked by the agent for lead {LeadId}: stage -> {Stage}.", leadId, parsed);
        return $"Lead stage updated to {parsed}.";
    }

    public async Task<string> AddCrmNote(
        [Description("One-line activity note text (under ~200 characters)")] string note,
        CancellationToken ct)
    {
        await crm.AddNoteAsync(leadId, note, ct);
        NoteWritten = true;
        logger.LogInformation("CRM tool 'add_crm_note' invoked by the agent for lead {LeadId}.", leadId);
        return "Note appended to the lead in the CRM.";
    }

    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(UpdateLeadStage, "update_lead_stage",
            "Move this lead to a CRM pipeline stage. Valid stages: New, Qualified, Contacted, Replied, Disqualified."),
        AIFunctionFactory.Create(AddCrmNote, "add_crm_note",
            "Append a short, one-line activity note to this lead in the CRM.")
    ];
}
