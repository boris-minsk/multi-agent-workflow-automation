namespace MultiAgent.Infrastructure.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.File;
}

public enum EmailProvider
{
    /// <summary>Writes a <c>.eml</c> file to the outbox; no network (default — no account required).</summary>
    File,

    /// <summary>Sends over real SMTP (Gmail-ready defaults) using an app password.</summary>
    Smtp
}

/// <summary>
/// Configuration for the real SMTP sender. Defaults target Gmail (smtp.gmail.com:587, STARTTLS).
/// The password is a provider app password (for Gmail, an "App Password" generated with 2FA on)
/// and must be supplied via env/user-secrets (e.g. <c>Smtp__Password</c>), never committed.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;

    /// <summary>587 → STARTTLS (true). 465 → implicit TLS (set false).</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP login — for Gmail, the full address.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>App password (never a real account password). Required when Provider=Smtp.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Envelope/header From address. Falls back to <see cref="Username"/> when blank.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "AI Sales Ops Assistant";
}
