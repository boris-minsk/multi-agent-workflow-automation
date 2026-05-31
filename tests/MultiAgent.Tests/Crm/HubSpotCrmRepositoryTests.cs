using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Crm;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Tests.Crm;

public class HubSpotCrmRepositoryTests
{
    [Fact]
    public void HubSpotIds_RoundTrip_RecoversContactId()
    {
        var guid = HubSpotIds.ToGuid(151_234_567L);

        Assert.True(HubSpotIds.TryGetContactId(guid, out var recovered));
        Assert.Equal(151_234_567L, recovered);
    }

    [Fact]
    public void HubSpotIds_RejectsForeignGuid()
    {
        Assert.False(HubSpotIds.TryGetContactId(Guid.NewGuid(), out _));
    }

    [Fact]
    public async Task ListAsync_MapsContactsToLeads_OrderedByCompany()
    {
        const string listJson = """
        {
          "results": [
            { "id": "201", "properties": { "email": "ann@acme.com", "firstname": "Ann", "lastname": "Lee", "company": "Acme", "website": "https://acme.com", "industry": "Tech", "hs_lead_status": "OPEN" }, "createdAt": "2024-01-01T00:00:00Z", "updatedAt": "2024-02-01T00:00:00Z" },
            { "id": "202", "properties": { "email": "bob@beta.io", "firstname": "Bob", "company": "Beta", "hs_lead_status": "NEW" } }
          ]
        }
        """;
        var (repo, _) = CreateRepo((req, _) =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/crm/v3/objects/contacts"
                ? (HttpStatusCode.OK, listJson)
                : (HttpStatusCode.NotFound, "{}"));

        var leads = await repo.ListAsync(CancellationToken.None);

        Assert.Equal(2, leads.Count);
        Assert.Equal("Acme", leads[0].CompanyName);   // ordered by company name
        Assert.Equal("Ann Lee", leads[0].ContactName);
        Assert.Equal("ann@acme.com", leads[0].ContactEmail);
        Assert.Equal("Tech", leads[0].Industry);
        Assert.Equal(LeadStage.Qualified, leads[0].Stage); // OPEN -> Qualified
        Assert.Equal(LeadStage.New, leads[1].Stage);       // NEW -> New
        Assert.True(HubSpotIds.TryGetContactId(leads[0].Id, out var id) && id == 201);
    }

    [Fact]
    public async Task GetAsync_RequestsContactByEncodedNumericId()
    {
        const string contactJson = """
        { "id": "305", "properties": { "email": "c@x.com", "firstname": "Cara", "company": "Cyber", "hs_lead_status": "CONNECTED" } }
        """;
        var (repo, handler) = CreateRepo((req, _) =>
            req.RequestUri!.AbsolutePath == "/crm/v3/objects/contacts/305"
                ? (HttpStatusCode.OK, contactJson)
                : (HttpStatusCode.NotFound, "{}"));

        var lead = await repo.GetAsync(HubSpotIds.ToGuid(305), CancellationToken.None);

        Assert.NotNull(lead);
        Assert.Equal(LeadStage.Replied, lead!.Stage); // CONNECTED -> Replied
        Assert.Contains(handler.Requests, r => r.Path == "/crm/v3/objects/contacts/305");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_OnForeignGuid()
    {
        var (repo, handler) = CreateRepo((_, _) => (HttpStatusCode.OK, "{}"));

        var lead = await repo.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(lead);
        Assert.Empty(handler.Requests); // never hits the API for a non-HubSpot id
    }

    [Fact]
    public async Task UpdateStageAsync_PatchesLeadStatus()
    {
        var (repo, handler) = CreateRepo((_, _) => (HttpStatusCode.OK, "{}"));

        await repo.UpdateStageAsync(HubSpotIds.ToGuid(77), LeadStage.Disqualified, CancellationToken.None);

        var patch = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal("/crm/v3/objects/contacts/77", patch.Path);
        Assert.Contains("hs_lead_status", patch.Body);
        Assert.Contains("UNQUALIFIED", patch.Body);
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenSchemaScopeMissing_SkipsWithoutThrowing()
    {
        // 403 on the property probe simulates a token without crm.schemas.contacts.* scope.
        var (repo, handler) = CreateRepo((req, _) =>
            req.RequestUri!.AbsolutePath.StartsWith("/crm/v3/properties/contacts")
                ? (HttpStatusCode.Forbidden, "{}")
                : (HttpStatusCode.OK, "{}"));

        await repo.UpdateScoreAsync(HubSpotIds.ToGuid(88), 9, Priority.High, "great fit", CancellationToken.None);

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Patch); // no write attempted
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenPropertiesExist_PatchesCustomProperties()
    {
        var (repo, handler) = CreateRepo((req, _) =>
        {
            // All custom properties already exist -> 200 on probe, 200 on patch.
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith("/crm/v3/properties/contacts"))
            {
                return (HttpStatusCode.OK, "{}");
            }
            return (HttpStatusCode.OK, "{}");
        });

        await repo.UpdateScoreAsync(HubSpotIds.ToGuid(99), 8, Priority.Medium, "good fit", CancellationToken.None);

        var patch = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal("/crm/v3/objects/contacts/99", patch.Path);
        Assert.Contains("multiagent_score", patch.Body);
        Assert.Contains("\"8\"", patch.Body);
        Assert.Contains("Medium", patch.Body);
        Assert.Contains("good fit", patch.Body);
    }

    [Fact]
    public async Task ListAsync_Throws_OnAuthError()
    {
        var (repo, _) = CreateRepo((_, _) => (HttpStatusCode.Unauthorized, """{"message":"bad token"}"""));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => repo.ListAsync(CancellationToken.None));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task AddNoteAsync_PostsNoteWithContactAssociation()
    {
        var (repo, handler) = CreateRepo((req, _) =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/crm/v3/objects/notes"
                ? (HttpStatusCode.Created, """{"id":"9001"}""")
                : (HttpStatusCode.NotFound, "{}"));

        await repo.AddNoteAsync(HubSpotIds.ToGuid(123), "Followed up via email", CancellationToken.None);

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("/crm/v3/objects/notes", post.Path);
        Assert.Contains("hs_note_body", post.Body);
        Assert.Contains("Followed up via email", post.Body);
        Assert.Contains("\"id\":\"123\"", post.Body);              // association points at the contact
        Assert.Contains("HUBSPOT_DEFINED", post.Body);
        Assert.Contains("\"associationTypeId\":202", post.Body);   // note -> contact association type
    }

    [Fact]
    public async Task AddNoteAsync_WhenApiFails_DoesNotThrow()
    {
        // Best-effort: a missing crm.objects.notes.write scope (403) must not fail the workflow run.
        var (repo, handler) = CreateRepo((_, _) => (HttpStatusCode.Forbidden, """{"message":"missing scope"}"""));

        await repo.AddNoteAsync(HubSpotIds.ToGuid(456), "note body", CancellationToken.None);

        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post); // attempted, then swallowed the 403
    }

    [Fact]
    public async Task AddNoteAsync_IgnoresForeignGuid_WithoutCallingApi()
    {
        var (repo, handler) = CreateRepo((_, _) => (HttpStatusCode.OK, "{}"));

        await repo.AddNoteAsync(Guid.NewGuid(), "note", CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetCrmHistoryAsync_JoinsPresentPropertiesInOrder()
    {
        const string contactJson = """
        { "id": "700", "properties": { "lifecyclestage": "lead", "hs_lead_status": "OPEN", "lastmodifieddate": "2024-02-01T00:00:00Z" } }
        """;
        var (repo, handler) = CreateRepo((req, _) =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/crm/v3/objects/contacts/700"
                ? (HttpStatusCode.OK, contactJson)
                : (HttpStatusCode.NotFound, "{}"));

        var history = await repo.GetCrmHistoryAsync(HubSpotIds.ToGuid(700), CancellationToken.None);

        Assert.Equal("Lifecycle stage: lead; Lead status: OPEN; Last modified: 2024-02-01T00:00:00Z", history);
        Assert.Contains(handler.Requests, r => r.Path == "/crm/v3/objects/contacts/700");
    }

    [Fact]
    public async Task GetCrmHistoryAsync_SkipsMissingAndBlankProperties()
    {
        // lifecyclestage absent entirely; lastmodifieddate present but blank -> only lead status survives.
        const string contactJson = """
        { "id": "701", "properties": { "hs_lead_status": "CONNECTED", "lastmodifieddate": "" } }
        """;
        var (repo, _) = CreateRepo((req, _) =>
            req.RequestUri!.AbsolutePath == "/crm/v3/objects/contacts/701"
                ? (HttpStatusCode.OK, contactJson)
                : (HttpStatusCode.NotFound, "{}"));

        var history = await repo.GetCrmHistoryAsync(HubSpotIds.ToGuid(701), CancellationToken.None);

        Assert.Equal("Lead status: CONNECTED", history);
    }

    [Fact]
    public async Task GetCrmHistoryAsync_ReturnsEmpty_OnForeignGuid()
    {
        var (repo, handler) = CreateRepo((_, _) => (HttpStatusCode.OK, "{}"));

        var history = await repo.GetCrmHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(string.Empty, history);
        Assert.Empty(handler.Requests); // never hits the API for a non-HubSpot id
    }

    [Fact]
    public async Task GetCrmHistoryAsync_ReturnsEmpty_WhenContactNotFound()
    {
        var (repo, _) = CreateRepo((_, _) => (HttpStatusCode.NotFound, "{}"));

        var history = await repo.GetCrmHistoryAsync(HubSpotIds.ToGuid(702), CancellationToken.None);

        Assert.Equal(string.Empty, history);
    }

    // --- helpers -------------------------------------------------------------

    private static (HubSpotCrmRepository Repo, StubHttpMessageHandler Handler) CreateRepo(
        Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body)> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.hubapi.com") };
        var options = Options.Create(new HubSpotOptions { AccessToken = "test-token" });
        var repo = new HubSpotCrmRepository(http, options, new HubSpotProvisioningState(), NullLogger<HubSpotCrmRepository>.Instance);
        return (repo, handler);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body)> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, body));

            var (status, responseBody) = responder(request, body);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
