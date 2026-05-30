using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Infrastructure.Notifications;

/// <summary>
/// Mocks "Slack" by logging to console (via ILogger) and persisting the message
/// to the Notifications table so the UI can render the feed.
/// </summary>
public sealed class ConsoleNotificationSink(
    IServiceScopeFactory scopeFactory,
    ILogger<ConsoleNotificationSink> logger) : INotificationSink
{
    public async Task PostAsync(Guid? runId, NotificationSeverity severity, string message, CancellationToken ct)
    {
        var level = severity switch
        {
            NotificationSeverity.Error => LogLevel.Error,
            NotificationSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        logger.Log(level, "[slack-console][{Severity}][run={RunId}] {Message}", severity, runId, message);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Notifications.Add(new NotificationLogEntry
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Channel = "slack-console",
            Severity = severity,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationLogEntry>> ListAsync(int take, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications
            .AsNoTracking()
            .OrderByDescending(n => n.Timestamp)
            .Take(take)
            .ToListAsync(ct);
    }
}
