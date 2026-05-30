using System.Text.Json;

namespace MultiAgent.Infrastructure.Llm;

/// <summary>
/// Produces deterministic canned JSON responses for each agent so the demo and tests
/// can run without an API key. Heuristics inspect the user message for buying-signal
/// keywords to keep results plausible.
/// </summary>
public static class MockResponseGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly string[] HighIntentKeywords =
    [
        "demo", "pricing", "rfp", "budget", "decision", "timeline", "asap",
        "switching", "implement", "procurement", "approved", "evaluate", "compare"
    ];

    private static readonly string[] MidIntentKeywords =
    [
        "interested", "webinar", "whitepaper", "downloaded", "newsletter",
        "consolidate", "discount", "tier", "follow-up", "demo for"
    ];

    private static readonly string[] LowIntentKeywords =
    [
        "free-tier", "tiny", "no engagement", "small", "grant", "depends on grant",
        "browsed", "one seat"
    ];

    public static string GenerateLeadScore(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var high = HighIntentKeywords.Count(k => lower.Contains(k));
        var mid = MidIntentKeywords.Count(k => lower.Contains(k));
        var low = LowIntentKeywords.Count(k => lower.Contains(k));

        var score = 5 + (high * 2) + mid - (low * 2);
        score = Math.Clamp(score, 1, 10);

        var priority = score switch
        {
            >= 8 => "High",
            >= 5 => "Medium",
            _ => "Low"
        };

        var industry = TryExtractField(userMessage, "industry") ?? "Unknown";

        var buyingIntent = high > 0 ? "Explicit buying signal (pricing/demo/RFP/timeline cues)"
            : mid > 0 ? "Moderate engagement (webinar, content downloads, general interest)"
            : "Low or cold engagement; no clear buying signal";

        var urgency = high >= 2 ? "Within 30 days"
            : high == 1 || mid >= 2 ? "Within 60-90 days"
            : "No clear timeline";

        var reason = high > 0
            ? $"Strong intent signals detected ({high} keyword matches): scored {score}/10."
            : mid > 0
                ? $"Mild engagement signals ({mid} matches); needs nurturing. Scored {score}/10."
                : $"Insufficient buying signal in CRM notes. Scored {score}/10.";

        var payload = new
        {
            Score = score,
            Priority = priority,
            Industry = industry,
            BuyingIntent = buyingIntent,
            Urgency = urgency,
            Reason = reason
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string GenerateResearchSummary(string userMessage)
    {
        var description = TryExtractField(userMessage, "companyDescription")
            ?? TryExtractField(userMessage, "industry")
            ?? "Mid-size organization (limited public information).";

        var painPoints = ExtractListField(userMessage, "knownPainPoints");
        if (painPoints.Count == 0)
        {
            painPoints = ["Manual processes slow down operations", "Disconnected systems force duplicate data entry"];
        }

        var news = ExtractListField(userMessage, "recentNews");
        if (news.Count == 0)
        {
            news = ["No major news in the last 90 days."];
        }

        var angles = new List<string>
        {
            $"Lead with the highest-value pain point: \"{painPoints[0]}\".",
            "Acknowledge recent activity to show research effort.",
            "Propose a 30-minute scoping call with a clear next step."
        };

        var payload = new
        {
            CompanyDescription = description,
            PainPoints = painPoints,
            RecentNews = news,
            SuggestedAngles = angles
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string GenerateOutreachDraft(string userMessage)
    {
        var contactName = TryExtractField(userMessage, "contactName") ?? "there";
        var company = TryExtractField(userMessage, "companyName") ?? "your team";
        var painPoints = ExtractListField(userMessage, "painPoints");
        var painPoint = painPoints.FirstOrDefault() ?? "the operational friction your team is navigating";

        var firstName = contactName.Split(' ').FirstOrDefault() ?? "there";

        var subject = $"Quick thought on {company} and {Truncate(painPoint.ToLowerInvariant(), 40)}";
        var body =
            $"Hi {firstName},\n\n" +
            $"I came across {company} while looking at how peer organizations are tackling {Truncate(painPoint.ToLowerInvariant(), 80)}. " +
            "A few of our customers had a similar setup before consolidating onto our platform, and they've shared concrete numbers on the impact.\n\n" +
            "Would a 20-minute call next week make sense? I can come prepared with a side-by-side based on what your team is using today.\n\n" +
            "Best,\nThe Sales Team";
        var nextAction = "Schedule a 20-minute discovery call within the next 7 days.";

        var payload = new
        {
            Subject = subject,
            Body = body,
            NextAction = nextAction
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string GenerateCrmUpdate(string userMessage)
    {
        var score = ExtractIntField(userMessage, "Score") ?? 5;
        var qualified = score >= 5;
        var targetStage = qualified ? "Contacted" : "Disqualified";
        var leadName = TryExtractField(userMessage, "companyName") ?? "lead";
        var shortNote = qualified
            ? $"Outreach drafted and sent. Score {score}/10. Awaiting reply."
            : $"Marked Disqualified after qualification (score {score}/10). No outreach sent.";

        var markdownNote =
            $"# {leadName} — Workflow run summary\n\n" +
            $"- Score: **{score}/10**\n" +
            $"- Target stage: **{targetStage}**\n" +
            $"- Outcome: {shortNote}\n\n" +
            "Full agent trace is available in the Web UI under Runs.";

        var payload = new
        {
            TargetStage = targetStage,
            ShortNote = shortNote,
            MarkdownNote = markdownNote
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? TryExtractField(string text, string fieldName)
    {
        // crude JSON-ish field extraction; sufficient for mock heuristics
        var marker = $"\"{fieldName}\"";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colon = text.IndexOf(':', idx);
        if (colon < 0) return null;
        var startQuote = text.IndexOf('"', colon + 1);
        if (startQuote < 0) return null;
        var endQuote = text.IndexOf('"', startQuote + 1);
        if (endQuote < 0) return null;
        return text.Substring(startQuote + 1, endQuote - startQuote - 1);
    }

    private static int? ExtractIntField(string text, string fieldName)
    {
        var marker = $"\"{fieldName}\"";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colon = text.IndexOf(':', idx);
        if (colon < 0) return null;
        var span = text.AsSpan(colon + 1).TrimStart();
        var end = 0;
        while (end < span.Length && (char.IsDigit(span[end]) || span[end] == '-')) end++;
        return end > 0 && int.TryParse(span[..end], out var v) ? v : null;
    }

    private static List<string> ExtractListField(string text, string fieldName)
    {
        var marker = $"\"{fieldName}\"";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return [];
        var open = text.IndexOf('[', idx);
        if (open < 0) return [];
        var close = text.IndexOf(']', open);
        if (close < 0) return [];
        var inner = text.Substring(open + 1, close - open - 1);

        var items = new List<string>();
        var i = 0;
        while (i < inner.Length)
        {
            var startQuote = inner.IndexOf('"', i);
            if (startQuote < 0) break;
            var endQuote = inner.IndexOf('"', startQuote + 1);
            if (endQuote < 0) break;
            items.Add(inner.Substring(startQuote + 1, endQuote - startQuote - 1));
            i = endQuote + 1;
        }
        return items;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "...";
}
