using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Infrastructure.Crm;

/// <summary>
/// <see cref="ICrmRepository"/> backed by the real HubSpot CRM v3 API. A HubSpot contact is
/// treated as a lead; its numeric id is encoded reversibly into the lead <see cref="Guid"/>
/// (see <see cref="HubSpotIds"/>). Reads use standard contact properties only. Stage writes map
/// to the standard <c>hs_lead_status</c>; score/priority/reason write to custom properties the
/// adapter provisions on first use. Writes are best-effort: a missing scope or transient error
/// is logged and never fails the workflow run; reads throw so misconfiguration surfaces clearly.
/// </summary>
public sealed class HubSpotCrmRepository : ICrmRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly HubSpotOptions _opts;
    private readonly HubSpotProvisioningState _state;
    private readonly ILogger<HubSpotCrmRepository> _logger;

    public HubSpotCrmRepository(
        HttpClient http,
        IOptions<HubSpotOptions> opts,
        HubSpotProvisioningState state,
        ILogger<HubSpotCrmRepository> logger)
    {
        _http = http;
        _opts = opts.Value;
        _state = state;
        _logger = logger;
    }

    public async Task<Lead?> GetAsync(Guid id, CancellationToken ct)
    {
        if (!HubSpotIds.TryGetContactId(id, out var contactId))
        {
            return null;
        }

        var contact = await FetchContactAsync(contactId, ct);
        return contact is null ? null : MapToLead(contact);
    }

    public async Task<IReadOnlyList<Lead>> ListAsync(CancellationToken ct)
    {
        var url = $"/crm/v3/objects/contacts?limit=100&archived=false&properties={Properties}";
        using var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(resp, ct);

        var dto = await resp.Content.ReadFromJsonAsync<HsListResponse>(JsonOpts, ct) ?? new HsListResponse();
        if (dto.Paging?.Next?.After is not null)
        {
            _logger.LogInformation(
                "HubSpot: more than 100 contacts exist; only the first page is listed (Phase 2 demo).");
        }

        return dto.Results.Select(MapToLead).OrderBy(l => l.CompanyName).ToList();
    }

    public async Task UpdateScoreAsync(Guid id, int score, Priority priority, string reason, CancellationToken ct)
    {
        if (!TryContactId(id, out var contactId))
        {
            return;
        }

        if (!await EnsureCustomPropertiesAsync(ct))
        {
            _logger.LogDebug(
                "HubSpot: skipping score write for contact {ContactId} — custom properties unavailable.",
                contactId);
            return;
        }

        var props = new Dictionary<string, string?>
        {
            [_opts.ScoreProperty] = score.ToString(CultureInfo.InvariantCulture),
            [_opts.PriorityProperty] = priority.ToString(),
            [_opts.ScoreReasonProperty] = reason
        };
        await PatchContactAsync(contactId, props, "score", ct);
    }

    public async Task UpdateStageAsync(Guid id, LeadStage stage, CancellationToken ct)
    {
        if (!TryContactId(id, out var contactId))
        {
            return;
        }

        var props = new Dictionary<string, string?>
        {
            [_opts.StageProperty] = HubSpotContactMap.ToLeadStatus(stage)
        };
        await PatchContactAsync(contactId, props, "stage", ct);
    }

    public async Task AddNoteAsync(Guid id, string note, CancellationToken ct)
    {
        if (!TryContactId(id, out var contactId))
        {
            return;
        }

        var body = new
        {
            properties = new Dictionary<string, string?>
            {
                ["hs_note_body"] = note,
                ["hs_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            },
            associations = new[]
            {
                new
                {
                    to = new { id = contactId.ToString(CultureInfo.InvariantCulture) },
                    // HUBSPOT_DEFINED note→contact association type id is 202.
                    types = new[] { new { associationCategory = "HUBSPOT_DEFINED", associationTypeId = 202 } }
                }
            }
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync("/crm/v3/objects/notes", body, JsonOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "HubSpot: note create for contact {ContactId} failed ({Status}) — needs the crm.objects.notes write scope? {Body}",
                    contactId, (int)resp.StatusCode, Truncate(content));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "HubSpot: note create for contact {ContactId} threw.", contactId);
        }
    }

    public async Task<string> GetCrmHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!HubSpotIds.TryGetContactId(id, out var contactId))
        {
            return string.Empty;
        }

        var contact = await FetchContactAsync(contactId, ct);
        if (contact is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddIfPresent(contact.Properties, "lifecyclestage", "Lifecycle stage", parts);
        AddIfPresent(contact.Properties, "hs_lead_status", "Lead status", parts);
        AddIfPresent(contact.Properties, "lastmodifieddate", "Last modified", parts);
        return string.Join("; ", parts);
    }

    // --- helpers -------------------------------------------------------------

    private static string Properties => string.Join(",", HubSpotContactMap.ReadProperties);

    private async Task<HsContact?> FetchContactAsync(long contactId, CancellationToken ct)
    {
        var url = $"/crm/v3/objects/contacts/{contactId}?archived=false&properties={Properties}";
        using var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<HsContact>(JsonOpts, ct);
    }

    private static Lead MapToLead(HsContact contact)
    {
        var p = contact.Properties;
        string? Get(string key) => p.TryGetValue(key, out var value) ? value : null;

        var email = Get("email") ?? string.Empty;
        var name = $"{Get("firstname")} {Get("lastname")}".Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = !string.IsNullOrWhiteSpace(email) ? email.Split('@')[0] : "(no name)";
        }

        var company = Get("company");
        var historyParts = new List<string>();
        AddIfPresent(p, "lifecyclestage", "Lifecycle", historyParts);
        AddIfPresent(p, "hs_lead_status", "Lead status", historyParts);

        return new Lead
        {
            Id = HubSpotIds.ToGuid(long.Parse(contact.Id, CultureInfo.InvariantCulture)),
            CompanyName = string.IsNullOrWhiteSpace(company) ? "(unknown company)" : company,
            ContactName = name,
            ContactEmail = email,
            Website = Get("website") ?? string.Empty,
            Industry = Get("industry"),
            Stage = HubSpotContactMap.FromLeadStatus(Get("hs_lead_status")),
            CrmNotes = string.Join("; ", historyParts),
            CreatedAt = contact.CreatedAt?.UtcDateTime ?? DateTime.UtcNow,
            UpdatedAt = contact.UpdatedAt?.UtcDateTime ?? DateTime.UtcNow
        };
    }

    private static void AddIfPresent(IReadOnlyDictionary<string, string?> props, string key, string label, List<string> parts)
    {
        if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value}");
        }
    }

    private async Task PatchContactAsync(long contactId, Dictionary<string, string?> props, string what, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PatchAsJsonAsync(
                $"/crm/v3/objects/contacts/{contactId}", new { properties = props }, JsonOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "HubSpot: {What} update for contact {ContactId} failed ({Status}): {Body}",
                    what, contactId, (int)resp.StatusCode, Truncate(content));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "HubSpot: {What} update for contact {ContactId} threw.", what, contactId);
        }
    }

    private bool TryContactId(Guid id, out long contactId)
    {
        if (HubSpotIds.TryGetContactId(id, out contactId))
        {
            return true;
        }

        _logger.LogWarning("HubSpot: lead id {Id} is not a HubSpot-derived id; ignoring write.", id);
        return false;
    }

    /// <summary>
    /// Ensures the custom contact properties exist, creating them once per process. Returns false
    /// (and logs a one-time hint) if the token lacks the <c>crm.schemas.contacts.*</c> scope, so
    /// score writes degrade gracefully instead of throwing.
    /// </summary>
    private async Task<bool> EnsureCustomPropertiesAsync(CancellationToken ct)
    {
        if (_state.CustomPropertiesAvailable is bool cached)
        {
            return cached;
        }

        await _state.Gate.WaitAsync(ct);
        try
        {
            if (_state.CustomPropertiesAvailable is bool cachedInner)
            {
                return cachedInner;
            }

            var available = await ProbeAndCreatePropertiesAsync(ct);
            _state.CustomPropertiesAvailable = available;
            return available;
        }
        finally
        {
            _state.Gate.Release();
        }
    }

    private async Task<bool> ProbeAndCreatePropertiesAsync(CancellationToken ct)
    {
        foreach (var (name, body) in CustomPropertyDefinitions())
        {
            using var get = await _http.GetAsync($"/crm/v3/properties/contacts/{name}", ct);
            if (get.StatusCode == HttpStatusCode.OK)
            {
                continue;
            }

            if (get.StatusCode == HttpStatusCode.Forbidden)
            {
                LogSchemaScopeMissing();
                return false;
            }

            if (get.StatusCode != HttpStatusCode.NotFound)
            {
                var probeBody = await get.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "HubSpot: probing custom property {Prop} returned {Status}: {Body}",
                    name, (int)get.StatusCode, Truncate(probeBody));
                return false;
            }

            using var create = await _http.PostAsJsonAsync("/crm/v3/properties/contacts", body, JsonOpts, ct);
            if (create.StatusCode == HttpStatusCode.Forbidden)
            {
                LogSchemaScopeMissing();
                return false;
            }

            if (!create.IsSuccessStatusCode)
            {
                var createBody = await create.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "HubSpot: could not create custom property {Prop} ({Status}): {Body}",
                    name, (int)create.StatusCode, Truncate(createBody));
                return false;
            }

            _logger.LogInformation("HubSpot: created custom contact property '{Prop}'.", name);
        }

        return true;
    }

    private (string Name, object Body)[] CustomPropertyDefinitions() =>
    [
        (_opts.ScoreProperty, new { name = _opts.ScoreProperty, label = "MultiAgent Score", type = "number", fieldType = "number", groupName = _opts.PropertyGroup }),
        (_opts.PriorityProperty, new { name = _opts.PriorityProperty, label = "MultiAgent Priority", type = "string", fieldType = "text", groupName = _opts.PropertyGroup }),
        (_opts.ScoreReasonProperty, new { name = _opts.ScoreReasonProperty, label = "MultiAgent Score Reason", type = "string", fieldType = "textarea", groupName = _opts.PropertyGroup })
    ];

    private void LogSchemaScopeMissing() => _logger.LogWarning(
        "HubSpot: cannot read/create custom properties (missing crm.schemas.contacts.* scope). " +
        "Stage updates still work; score/priority/reason will not be written to HubSpot. " +
        "Add the schema scopes to the Service Key to enable them.");

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"HubSpot {(int)resp.StatusCode} {resp.RequestMessage?.Method} " +
            $"{resp.RequestMessage?.RequestUri?.PathAndQuery}: {Truncate(body)}");
    }

    private static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max] + "…";
}
