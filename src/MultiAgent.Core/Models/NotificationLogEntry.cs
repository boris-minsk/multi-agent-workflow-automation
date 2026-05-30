namespace MultiAgent.Core.Models;

/// <summary>
/// One posted alert/status message: severity, channel (slack-console), text.
/// </summary>
public sealed class NotificationLogEntry
{
    public Guid Id { get; set; }
    public Guid? RunId { get; set; }
    public string Channel { get; set; } = "slack-console";
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
