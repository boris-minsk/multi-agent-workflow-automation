using MultiAgent.Core.Models;
using MultiAgent.Mcp;
using MultiAgent.Tests.TestFixtures;

namespace MultiAgent.Tests.Mcp;

public class CrmMcpToolsTests
{
    [Fact]
    public async Task UpdateLeadStage_ValidStage_CallsRepo()
    {
        var crm = new RecordingCrmRepository();
        var id = Guid.NewGuid();

        var message = await CrmMcpTools.UpdateLeadStage(crm, id.ToString(), "Replied", CancellationToken.None);

        var update = Assert.Single(crm.StageUpdates);
        Assert.Equal(id, update.Id);
        Assert.Equal(LeadStage.Replied, update.Stage);
        Assert.Contains("Replied", message);
    }

    [Fact]
    public async Task UpdateLeadStage_InvalidStage_ReturnsError_NoRepoCall()
    {
        var crm = new RecordingCrmRepository();

        var message = await CrmMcpTools.UpdateLeadStage(crm, Guid.NewGuid().ToString(), "Frobnicate", CancellationToken.None);

        Assert.Empty(crm.StageUpdates);
        Assert.Contains("not a valid stage", message);
    }

    [Fact]
    public async Task UpdateLeadStage_InvalidGuid_ReturnsError_NoRepoCall()
    {
        var crm = new RecordingCrmRepository();

        var message = await CrmMcpTools.UpdateLeadStage(crm, "not-a-guid", "Contacted", CancellationToken.None);

        Assert.Empty(crm.StageUpdates);
        Assert.Contains("not a valid lead id", message);
    }

    [Fact]
    public async Task GetLead_Found_ReturnsJsonWithCompany()
    {
        var id = Guid.NewGuid();
        var crm = new RecordingCrmRepository { LeadToReturn = new Lead { Id = id, CompanyName = "Acme Robotics" } };

        var message = await CrmMcpTools.GetLead(crm, id.ToString(), CancellationToken.None);

        Assert.Contains("Acme Robotics", message);
    }

    [Fact]
    public async Task GetLead_NotFound_ReturnsMessage()
    {
        var crm = new RecordingCrmRepository { LeadToReturn = null };

        var message = await CrmMcpTools.GetLead(crm, Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.Contains("No lead found", message);
    }

    [Fact]
    public async Task AddCrmNote_CallsRepo()
    {
        var crm = new RecordingCrmRepository();
        var id = Guid.NewGuid();

        await CrmMcpTools.AddCrmNote(crm, id.ToString(), "Left a voicemail.", CancellationToken.None);

        var note = Assert.Single(crm.Notes);
        Assert.Equal(id, note.Id);
        Assert.Equal("Left a voicemail.", note.Note);
    }
}
