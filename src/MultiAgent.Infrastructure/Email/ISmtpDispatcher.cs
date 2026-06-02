using MimeKit;

namespace MultiAgent.Infrastructure.Email;

/// <summary>
/// The SMTP network boundary: sends a fully-built MIME message. Extracted as a seam so
/// <see cref="SmtpEmailSender"/> can be unit-tested without a live SMTP server (tests inject
/// a recording fake); the production implementation is <see cref="MailKitSmtpDispatcher"/>.
/// </summary>
internal interface ISmtpDispatcher
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}
