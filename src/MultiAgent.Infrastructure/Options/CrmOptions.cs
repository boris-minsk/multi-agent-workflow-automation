namespace MultiAgent.Infrastructure.Options;

public sealed class CrmOptions
{
    public const string SectionName = "Crm";

    public CrmProvider Provider { get; set; } = CrmProvider.Sqlite;
}

public enum CrmProvider
{
    /// <summary>In-memory/SQLite mock CRM (default — no external account required).</summary>
    Sqlite,

    /// <summary>Real HubSpot CRM via the v3 API and a private-app/Service-Key bearer token.</summary>
    HubSpot
}

/// <summary>
/// Configuration for the HubSpot CRM adapter. The access token is a HubSpot
/// Service Key (or legacy private-app token) and must be supplied via env/user-secrets
/// (e.g. <c>HubSpot__AccessToken</c>), never committed.
/// </summary>
public sealed class HubSpotOptions
{
    public const string SectionName = "HubSpot";

    public string AccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.hubapi.com";

    /// <summary>Standard contact property used to reflect <c>LeadStage</c>. Writable with <c>crm.objects.contacts.write</c>.</summary>
    public string StageProperty { get; set; } = "hs_lead_status";

    // Custom contact properties the adapter provisions for its own data.
    // Provisioning needs crm.schemas.contacts.* ; absent that, score writes degrade gracefully.
    public string ScoreProperty { get; set; } = "multiagent_score";
    public string PriorityProperty { get; set; } = "multiagent_priority";
    public string ScoreReasonProperty { get; set; } = "multiagent_score_reason";

    /// <summary>Existing HubSpot property group to file the custom properties under (avoids creating a group).</summary>
    public string PropertyGroup { get; set; } = "contactinformation";
}
