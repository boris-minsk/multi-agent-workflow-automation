using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
/// Post a status/alert message (Slack stand-in) and list recent ones.
/// </summary>
public interface INotificationSink
{
    Task PostAsync(Guid? runId, NotificationSeverity severity, string message, CancellationToken ct);
    Task<IReadOnlyList<NotificationLogEntry>> ListAsync(int take, CancellationToken ct);
}
