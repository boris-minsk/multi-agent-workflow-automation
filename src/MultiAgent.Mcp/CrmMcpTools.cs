using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Mcp;

/// <summary>
/// MCP tools exposing the CRM — Get / UpdateStage / AddNote — over the same
/// <see cref="ICrmRepository"/> the API uses (mock SQLite or real HubSpot per Crm:Provider).
/// The repository is injected per call from the host DI; lead ids are GUID strings. These are
/// the same three operations the in-app CrmUpdate agent calls as tools, now reachable from any
/// MCP client (Claude Desktop, Cursor, Claude Code).
/// </summary>
[McpServerToolType]
public static class CrmMcpTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool, Description("Fetch a CRM lead/contact by its id (GUID) and return its details as JSON.")]
    public static async Task<string> GetLead(
        ICrmRepository crm,
        [Description("Lead id (GUID)")] string leadId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(leadId, out var id))
        {
            return $"'{leadId}' is not a valid lead id (expected a GUID).";
        }

        var lead = await crm.GetAsync(id, ct);
        return lead is null
            ? $"No lead found with id {id}."
            : JsonSerializer.Serialize(lead, Json);
    }

    [McpServerTool, Description("Move a lead to a CRM pipeline stage. Valid stages: New, Qualified, Contacted, Replied, Disqualified.")]
    public static async Task<string> UpdateLeadStage(
        ICrmRepository crm,
        [Description("Lead id (GUID)")] string leadId,
        [Description("Target stage: New, Qualified, Contacted, Replied, or Disqualified")] string stage,
        CancellationToken ct)
    {
        if (!Guid.TryParse(leadId, out var id))
        {
            return $"'{leadId}' is not a valid lead id (expected a GUID).";
        }
        if (!Enum.TryParse<LeadStage>(stage, ignoreCase: true, out var parsed))
        {
            return $"'{stage}' is not a valid stage. Valid values: New, Qualified, Contacted, Replied, Disqualified.";
        }

        await crm.UpdateStageAsync(id, parsed, ct);
        return $"Lead {id} stage set to {parsed}.";
    }

    [McpServerTool, Description("Append a short, one-line activity note to a lead in the CRM.")]
    public static async Task<string> AddCrmNote(
        ICrmRepository crm,
        [Description("Lead id (GUID)")] string leadId,
        [Description("One-line note text")] string note,
        CancellationToken ct)
    {
        if (!Guid.TryParse(leadId, out var id))
        {
            return $"'{leadId}' is not a valid lead id (expected a GUID).";
        }

        await crm.AddNoteAsync(id, note, ct);
        return $"Note added to lead {id}.";
    }
}
