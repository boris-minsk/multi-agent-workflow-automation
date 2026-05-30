namespace MultiAgent.Core.Models;

/// <summary>
/// A sent email: to/subject/body + the .eml file path on disk.
/// </summary>
public sealed class OutboxItem
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid LeadId { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
