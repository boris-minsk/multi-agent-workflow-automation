using MultiAgent.Core.Models;

namespace MultiAgent.Infrastructure.Crm;

/// <summary>
/// Property names and <see cref="LeadStage"/> ↔ HubSpot <c>hs_lead_status</c> mapping
/// for the HubSpot contact adapter.
/// </summary>
internal static class HubSpotContactMap
{
    /// <summary>
    /// Standard contact properties read for each lead. All exist by default, so requesting
    /// them never triggers a <c>PROPERTY_DOESNT_EXIST</c> error (custom props are written, not read).
    /// </summary>
    public static readonly string[] ReadProperties =
    [
        "email", "firstname", "lastname", "company", "website", "industry",
        "lifecyclestage", "hs_lead_status", "createdate", "lastmodifieddate"
    ];

    public static string ToLeadStatus(LeadStage stage) => stage switch
    {
        LeadStage.New => "NEW",
        LeadStage.Qualified => "OPEN",
        LeadStage.Contacted => "ATTEMPTED_TO_CONTACT",
        LeadStage.Replied => "CONNECTED",
        LeadStage.Disqualified => "UNQUALIFIED",
        _ => "NEW"
    };

    public static LeadStage FromLeadStatus(string? status) => status switch
    {
        "NEW" => LeadStage.New,
        "OPEN" => LeadStage.Qualified,
        "IN_PROGRESS" => LeadStage.Contacted,
        "ATTEMPTED_TO_CONTACT" => LeadStage.Contacted,
        "CONNECTED" => LeadStage.Replied,
        "OPEN_DEAL" => LeadStage.Replied,
        "UNQUALIFIED" => LeadStage.Disqualified,
        "BAD_TIMING" => LeadStage.Disqualified,
        _ => LeadStage.New
    };
}
