using MultiAgent.Core.Models;

namespace MultiAgent.Core.Abstractions;

/// <summary>
/// Writes to the outbox and read back what was sent.
/// </summary>
public interface IEmailSender
{
    Task<OutboxItem> SendAsync(
        Guid runId,
        Guid leadId,
        string to,
        string subject,
        string body,
        CancellationToken ct);

    Task<IReadOnlyList<OutboxItem>> ListAsync(int take, CancellationToken ct);
    Task<OutboxItem?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// True if an email has already been sent for this run (an outbox row exists). Used by startup
    /// recovery to decide whether an interrupted run can be safely re-run without a duplicate send.
    /// </summary>
    Task<bool> HasSentForRunAsync(Guid runId, CancellationToken ct);
}
