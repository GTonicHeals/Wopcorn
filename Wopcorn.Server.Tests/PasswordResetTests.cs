using System.Net;
using System.Net.Http.Json;

namespace Wopcorn.Server.Tests;

/// <summary>
/// API-CONTRACT.md "Password reset". The obligations are the 202-for-everything
/// rule, the single-use token, and the split between a dead token and a rejected
/// password.
/// </summary>
public class PasswordResetTests
{
    private static (WopcornApiFactory Factory, FakeResetMailer Mailer) NewFactory()
    {
        var mailer = new FakeResetMailer();
        return (new WopcornApiFactory { ResetMailer = mailer }, mailer);
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> ResetAsync(
        HttpClient client, string email, string token, string password) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { email, token, password });

    [Fact]
    public async Task Unknown_and_known_email_answer_identically()
    {
        var (factory, mailer) = NewFactory();
        using var _ = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("known@example.com", "password1", "known-reset");

        using var anonymous = factory.CreateAnonymousClient();
        var known = await ForgotAsync(anonymous, "known@example.com");
        var unknown = await ForgotAsync(anonymous, "nobody@example.com");

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);

        // Identical to the caller — the only difference is invisible, on the inside.
        Assert.Equal(0, (await known.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(0, (await unknown.Content.ReadAsByteArrayAsync()).Length);

        Assert.Single(mailer.Sends);
        Assert.Equal("known@example.com", mailer.Sends.Single().Email);
    }

    [Fact]
    public async Task A_mailed_token_resets_the_password_and_the_new_one_works()
    {
        var (factory, mailer) = NewFactory();
        using var _ = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("cycle@example.com", "password1", "cycle");

        using var anonymous = factory.CreateAnonymousClient();
        await ForgotAsync(anonymous, "cycle@example.com");

        var (email, token) = mailer.SingleLinkFor("cycle@example.com");

        var reset = await ResetAsync(anonymous, email, token, "brand-new-password");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The reset must not itself mint a session (API-CONTRACT.md).
        Assert.False(reset.Headers.Contains("Set-Cookie"));

        var withNew = await anonymous.LoginAsync("cycle@example.com", "brand-new-password");
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        var withOld = await anonymous.LoginAsync("cycle@example.com", "password1");
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_spent_twice()
    {
        var (factory, mailer) = NewFactory();
        using var _ = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("once@example.com", "password1", "once");

        using var anonymous = factory.CreateAnonymousClient();
        await ForgotAsync(anonymous, "once@example.com");
        var (email, token) = mailer.SingleLinkFor("once@example.com");

        var first = await ResetAsync(anonymous, email, token, "first-new-password");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await ResetAsync(anonymous, email, token, "second-new-password");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("invalid_reset_token", (await second.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_garbage_token_is_invalid_reset_token()
    {
        var (factory, _) = NewFactory();
        using var factoryScope = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("garbage@example.com", "password1", "garbage");

        using var anonymous = factory.CreateAnonymousClient();
        var response = await ResetAsync(
            anonymous, "garbage@example.com", "not-a-real-token", "another-password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_reset_token", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task An_unknown_email_on_reset_looks_like_a_bad_token()
    {
        var (factory, _) = NewFactory();
        using var factoryScope = factory;

        using var anonymous = factory.CreateAnonymousClient();
        var response = await ResetAsync(
            anonymous, "ghost@example.com", "not-a-real-token", "another-password");

        // Not a 404: the reset endpoint must not confirm an address either.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_reset_token", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_good_token_with_a_short_password_is_a_field_error()
    {
        var (factory, mailer) = NewFactory();
        using var _ = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("weak@example.com", "password1", "weak");

        using var anonymous = factory.CreateAnonymousClient();
        await ForgotAsync(anonymous, "weak@example.com");
        var (email, token) = mailer.SingleLinkFor("weak@example.com");

        var response = await ResetAsync(anonymous, email, token, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadApiErrorAsync();
        // The token was fine, so this is the user's typing and belongs on a field.
        Assert.Equal("validation_failed", error.Code);
        Assert.NotNull(error.Errors);
        Assert.Contains(error.Errors!, e => e.Key.Equals("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_link_carries_the_configured_origin_and_a_usable_token()
    {
        var (factory, mailer) = NewFactory();
        using var _ = factory;

        using var session = factory.CreateSessionClient();
        await session.RegisterAndReadAsync("link@example.com", "password1", "link");

        using var anonymous = factory.CreateAnonymousClient();
        await ForgotAsync(anonymous, "link@example.com");

        var sent = Assert.Single(mailer.Sends);
        // Smtp:AppBaseUrl wins over the request origin — the browser is not
        // necessarily on the port the request arrived on.
        Assert.StartsWith("https://wopcorn.test/reset-password?", sent.ResetUrl, StringComparison.Ordinal);
        Assert.Equal("link", sent.DisplayName);

        var (email, token) = mailer.SingleLinkFor("link@example.com");
        Assert.Equal("link@example.com", email);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }
}
