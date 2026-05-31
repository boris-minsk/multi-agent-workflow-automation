using System.Text.Json.Serialization;

namespace MultiAgent.Infrastructure.Crm;

// Minimal DTOs for the HubSpot CRM v3 contacts API responses we consume.

internal sealed class HsContact
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("properties")] public Dictionary<string, string?> Properties { get; set; } = [];
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class HsListResponse
{
    [JsonPropertyName("results")] public List<HsContact> Results { get; set; } = [];
    [JsonPropertyName("paging")] public HsPaging? Paging { get; set; }
}

internal sealed class HsPaging
{
    [JsonPropertyName("next")] public HsPagingNext? Next { get; set; }
}

internal sealed class HsPagingNext
{
    [JsonPropertyName("after")] public string? After { get; set; }
}
