using System.Collections.Concurrent;
using System.Web;
using Wopcorn.Server.Auth;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Captures reset mails instead of sending them, so a test can follow the link
/// the user would have clicked. Nothing here touches a network.
/// </summary>
public class FakeResetMailer : IPasswordResetMailer
{
    public record Sent(string Email, string DisplayName, string ResetUrl);

    private readonly ConcurrentQueue<Sent> _sent = new();

    public IReadOnlyCollection<Sent> Sends => _sent;

    public Task SendAsync(
        string email, string displayName, string resetUrl, CancellationToken ct = default)
    {
        _sent.Enqueue(new Sent(email, displayName, resetUrl));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The single mail sent to <paramref name="email"/>, with its link already
    /// pulled apart. Fails the test if there is not exactly one.
    /// </summary>
    public (string Email, string Token) SingleLinkFor(string email)
    {
        var matches = _sent.Where(s => s.Email == email).ToList();
        var sent = Assert.Single(matches);

        var query = HttpUtility.ParseQueryString(new Uri(sent.ResetUrl).Query);
        var linkEmail = query["email"];
        var token = query["token"];

        Assert.NotNull(linkEmail);
        Assert.NotNull(token);
        return (linkEmail, token);
    }
}
