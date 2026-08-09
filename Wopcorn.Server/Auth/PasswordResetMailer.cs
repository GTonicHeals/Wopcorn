using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Wopcorn.Server.Auth;

public interface IPasswordResetMailer
{
    /// <summary>
    /// Delivers a reset link. Never throws: <c>POST /api/auth/forgot-password</c>
    /// answers <c>202</c> whatever happens here, so a mail failure must not turn
    /// into a 500 that tells the caller the address existed.
    /// </summary>
    Task SendAsync(string email, string displayName, string resetUrl, CancellationToken ct = default);
}

/// <summary>
/// Sends over SMTP when <see cref="SmtpOptions.Host"/> is set, and logs the link
/// when it is not (API-CONTRACT.md, "Password reset").
/// </summary>
public class PasswordResetMailer(
    IOptions<SmtpOptions> options,
    ILogger<PasswordResetMailer> logger) : IPasswordResetMailer
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(
        string email, string displayName, string resetUrl, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // The dev fallback. This is the only place a reset link is ever
            // written anywhere but an inbox, and it is gated on there being no
            // mail server at all — a configured deployment never logs one.
            logger.LogInformation(
                "SMTP is not configured; password reset link for {Email}: {ResetUrl}",
                email, resetUrl);
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = "Reset your Wopcorn password",
                Body = Body(displayName, resetUrl),
                IsBodyHtml = false,
            };
            message.To.Add(new MailAddress(email));

            using var client = new SmtpClient(_options.Host!, _options.Port)
            {
                EnableSsl = _options.UseStartTls,
            };

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
            }

            await client.SendMailAsync(message, ct);
            logger.LogInformation("Password reset mail sent to {Email}.", email);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose — see the interface note. Logged at Error so an
            // operator still finds out the mail path is broken.
            logger.LogError(ex, "Failed to send the password reset mail to {Email}.", email);
        }
    }

    private static string Body(string displayName, string resetUrl) =>
        $"""
         Hi {displayName},

         Someone asked to reset the password on your Wopcorn account. Open this
         link to choose a new one:

         {resetUrl}

         The link can be used once and expires shortly. If this wasn't you, you
         can ignore this mail — nothing has changed.

         — Wopcorn
         """;
}
