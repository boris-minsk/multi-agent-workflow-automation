namespace MultiAgent.Core.Models;

/// <summary>
/// The prospect: company/contact/website, plus workflow-filled fields (stage, score, priority, score reason, CRM notes).
/// </summary>
public sealed class Lead
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string CrmNotes { get; set; } = string.Empty;
    public LeadStage Stage { get; set; } = LeadStage.New;
    public int? Score { get; set; }
    public Priority? Priority { get; set; }
    public string? ScoreReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
