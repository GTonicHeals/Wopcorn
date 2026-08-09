namespace Wopcorn.Server.Auth;

/// <summary>
/// Outbound mail settings, bound from the <c>Smtp</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="Host"/> is the switch: leave it empty and nothing is sent — the
/// reset link is written to the log instead. That is the whole of the dev
/// fallback, and it means a developer never needs a mail server to walk the
/// reset flow end to end.
///
/// Credentials belong in user secrets, next to the TMDB ones — never in
/// appsettings.json.
/// </remarks>
public class SmtpOptions
{
    public const string Section = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS. Off only for a mail relay on the same trusted LAN.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? UserName { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@wopcorn.local";
    public string FromName { get; set; } = "Wopcorn";

    /// <summary>
    /// Absolute origin the reset link points at (e.g. <c>https://wopcorn.lan</c>).
    /// Empty means "use the origin the request arrived on", which is right for a
    /// same-origin production deployment. It is worth setting explicitly in
    /// development, where the browser is on the Vite port and the request reaches
    /// Kestrel on another.
    /// </summary>
    public string? AppBaseUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
