using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MultiAgent.Infrastructure.Options;

namespace MultiAgent.Infrastructure.Email;

/// <summary>
/// Production <see cref="ISmtpDispatcher"/> backed by MailKit. Opens a short-lived connection
/// per send (connect → authenticate → send → quit), so it is safe to register as a singleton.
/// </summary>
internal sealed class MailKitSmtpDispatcher(
    IOptions<SmtpOptions> options,
    ILogger<MailKitSmtpDispatcher> logger) : ISmtpDispatcher
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var security = _options.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.SslOnConnect;

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, security, ct);
        await client.AuthenticateAsync(_options.Username, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Sent email via SMTP {Host}:{Port} to {To}.",
            _options.Host, _options.Port, message.To.ToString());
    }
}
