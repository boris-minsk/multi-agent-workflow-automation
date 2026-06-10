using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using MultiAgent.Infrastructure.Options;
using MultiAgent.Infrastructure.Persistence;

namespace MultiAgent.Infrastructure.Email;

public sealed class FileSystemEmailSender(
    AppDbContext db,
    IOptions<PathsOptions> pathsOptions,
    ILogger<FileSystemEmailSender> logger) : IEmailSender
{
    public async Task<OutboxItem> SendAsync(
        Guid runId,
        Guid leadId,
        string to,
        string subject,
        string body,
        CancellationToken ct)
    {
        var outboxDir = pathsOptions.Value.OutboxDirectory;
        Directory.CreateDirectory(outboxDir);

        var item = new OutboxItem
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            LeadId = leadId,
            ToAddress = to,
            Subject = subject,
            Body = body,
            GeneratedAt = DateTime.UtcNow,
            FilePath = Path.Combine(outboxDir, $"{runId:N}.eml")
        };

        var eml = new StringBuilder()
            .Append("From: sales-bot@multiagent.local\r\n")
            .Append("To: ").Append(to).Append("\r\n")
            .Append("Subject: ").Append(subject).Append("\r\n")
            .Append("Date: ").Append(item.GeneratedAt.ToString("R")).Append("\r\n")
            .Append("X-MultiAgent-RunId: ").Append(runId).Append("\r\n")
            .Append("X-MultiAgent-LeadId: ").Append(leadId).Append("\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n\r\n")
            .Append(body);

        await File.WriteAllTextAsync(item.FilePath, eml.ToString(), ct);

        db.OutboxItems.Add(item);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Wrote outbox email {OutboxId} to {Path} for run {RunId}.",
            item.Id, item.FilePath, runId);
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
