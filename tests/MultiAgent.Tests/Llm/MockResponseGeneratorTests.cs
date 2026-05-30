using System.Text.Json;
using MultiAgent.Infrastructure.Llm;

namespace MultiAgent.Tests.Llm;

public class MockResponseGeneratorTests
{
    [Fact]
    public void GenerateLeadScore_HighIntent_ProducesHighScore()
    {
        var userMessage =
            "\"CrmNotes\":\"VP of Operations booked a demo for next Tuesday. Procurement said budget is approved.\",\"Industry\":\"Industrial Automation\"";

        var json = MockResponseGenerator.GenerateLeadScore(userMessage);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.GetProperty("Score").GetInt32() >= 8,
            $"Expected high score, got {doc.GetProperty("Score").GetInt32()}");
        Assert.Equal("High", doc.GetProperty("Priority").GetString());
    }

    [Fact]
    public void GenerateLeadScore_LowIntent_ProducesLowScore()
    {
        var userMessage =
            "\"CrmNotes\":\"Browsed pricing page once. No email engagement. Free-tier user with one seat.\"";

        var json = MockResponseGenerator.GenerateLeadScore(userMessage);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.GetProperty("Score").GetInt32() <= 4,
            $"Expected low score, got {doc.GetProperty("Score").GetInt32()}");
        Assert.Equal("Low", doc.GetProperty("Priority").GetString());
    }

    [Fact]
    public void GenerateOutreachDraft_UsesContactFirstName()
    {
        var userMessage =
            "\"ContactName\":\"Yuki Tanaka\",\"CompanyName\":\"Acme Robotics\"," +
            "\"PainPoints\":[\"slow quoting cycle\",\"manual reporting\"]";

        var json = MockResponseGenerator.GenerateOutreachDraft(userMessage);
        var doc = JsonDocument.Parse(json).RootElement;

        var body = doc.GetProperty("Body").GetString();
        Assert.NotNull(body);
        Assert.Contains("Yuki", body);
        Assert.Contains("Acme Robotics", body);
        Assert.Contains("slow quoting cycle", body!);
    }

    [Fact]
    public void GenerateCrmUpdate_HighScoreSetsContacted()
    {
        var json = MockResponseGenerator.GenerateCrmUpdate("\"Score\":9,\"CompanyName\":\"Acme\"");
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Contacted", doc.GetProperty("TargetStage").GetString());
        Assert.Contains("Acme", doc.GetProperty("MarkdownNote").GetString()!);
    }

    [Fact]
    public void GenerateCrmUpdate_LowScoreSetsDisqualified()
    {
        var json = MockResponseGenerator.GenerateCrmUpdate("\"Score\":3,\"CompanyName\":\"Acme\"");
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Disqualified", doc.GetProperty("TargetStage").GetString());
    }
}
