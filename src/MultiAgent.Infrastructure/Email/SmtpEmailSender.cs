using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Options;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Infrastructure.Email;

/// <summary>
/// Real SMTP <see cref="IEmailSender"/> (Gmail-ready defaults), selected via <c>Email:Provider=Smtp</c>.
/// Sends the drafted email over SMTP, then writes the same <see cref="OutboxItem"/> row and a
/// <c>.eml</c> audit file the file mock produces, so the dashboard/outbox stay consistent across
/// providers. Send-first: the row + file are persisted only after a successful send, so the outbox
/// reflects delivered mail and a transport failure propagates to fail the run.
/// See <see cref="FileSystemEmailSender"/> for the default no-network mock.
/// </summary>
internal sealed class SmtpEmailSender(
    ISmtpDispatcher dispatcher,
    AppDbContext db,
    IOptions<SmtpOptions> smtpOptions,
    IOptions<PathsOptions> pathsOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _smtp = smtpOptions.Value;

    public async Task<OutboxItem> SendAsync(
        Guid runId,
        Guid leadId,
        string to,
        string subject,
        string body,
        CancellationToken ct)
    {
        var fromAddress = string.IsNullOrWhiteSpace(_smtp.FromAddress) ? _smtp.Username : _smtp.FromAddress;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        message.Headers.Add("X-MultiAgent-RunId", runId.ToString());
        message.Headers.Add("X-MultiAgent-LeadId", leadId.ToString());

        // Send first; only record the outbox row + .eml audit on success so the outbox reflects
        // delivered mail. A transport failure throws here and the workflow marks the run Failed.
        await dispatcher.SendAsync(message, ct);

        var outboxDir = pathsOptions.Value.OutboxDirectory;
        Directory.CreateDirectory(outboxDir);
        var filePath = Path.Combine(outboxDir, $"{runId:N}.eml");
        await using (var stream = File.Create(filePath))
        {
            await message.WriteToAsync(stream, ct);
        }

        var item = new OutboxItem
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            LeadId = leadId,
            ToAddress = to,
            Subject = subject,
            Body = body,
            GeneratedAt = DateTime.UtcNow,
            FilePath = filePath
        };

        db.OutboxItems.Add(item);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Sent SMTP email {OutboxId} to {To} for run {RunId}; audit copy at {Path}.",
            item.Id, to, runId, filePath);
        return item;
    }

    public async Task<IReadOnlyList<OutboxItem>> ListAsync(int take, CancellationToken ct) =>
        await db.OutboxItems
            .AsNoTracking()
            .OrderByDescending(o => o.GeneratedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<OutboxItem?> GetAsync(Guid id, CancellationToken ct) =>
        await db.OutboxItems.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<bool> HasSentForRunAsync(Guid runId, CancellationToken ct) =>
        await db.OutboxItems.AsNoTracking().AnyAsync(o => o.RunId == runId, ct);
}
