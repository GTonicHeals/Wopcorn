using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wopcorn.Server.Tests;

/// <summary>
/// API-CONTRACT.md "Passkeys".
///
/// What is *not* here: a completed registration or sign-in. Both require a real
/// authenticator to sign a challenge, which no amount of test plumbing can fake
/// without reimplementing WebAuthn's cryptography — that is browser territory.
/// These cover everything on this side of that line: the challenge endpoints, the
/// shape of what they hand back, ownership, and the failure answers.
/// </summary>
public class PasskeyTests(WopcornApiFactory factory) : IClassFixture<WopcornApiFactory>
{
    private record OptionsDto(string OptionsJson);

    private record PasskeyDto(string Id, string Name, DateTimeOffset CreatedAt, bool IsBackedUp);

    private static Task<HttpResponseMessage> RequestOptionsAsync(HttpClient client, string? email) =>
        client.PostAsJsonAsync("/api/auth/passkeys/request-options", new { email });

    [Fact]
    public async Task Request_options_are_anonymous_and_usernameless()
    {
        using var anonymous = factory.CreateAnonymousClient();

        var response = await RequestOptionsAsync(anonymous, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var options = await response.ReadAsAsync<OptionsDto>();
        // A JSON-encoded string, not an object — the contract is explicit that this
        // passes through to navigator.credentials untouched.
        using var parsed = JsonDocument.Parse(options.OptionsJson);
        Assert.True(parsed.RootElement.TryGetProperty("challenge", out _));
    }

    [Fact]
    public async Task Request_options_for_an_unknown_email_still_return_options()
    {
        using var anonymous = factory.CreateAnonymousClient();

        var response = await RequestOptionsAsync(anonymous, "nobody-at-all@example.com");

        // Not a 404 — that would confirm which addresses have accounts.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.ReadAsAsync<OptionsDto>();
        Assert.False(string.IsNullOrWhiteSpace(options.OptionsJson));
    }

    [Fact]
    public async Task Request_options_set_the_challenge_state_cookie()
    {
        using var anonymous = factory.CreateAnonymousClient();

        var response = await RequestOptionsAsync(anonymous, null);

        // The two calls are one exchange: without this cookie coming back on the
        // signin call there is nothing to check the assertion against.
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_bogus_assertion_is_401_not_a_500()
    {
        using var anonymous = factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/auth/passkeys/signin", new { credentialJson = "{}" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Managing_passkeys_needs_a_session()
    {
        using var anonymous = factory.CreateAnonymousClient();

        var list = await anonymous.GetAsync("/api/me/passkeys");
        var create = await anonymous.PostAsync("/api/me/passkeys/creation-options", null);
        var remove = await anonymous.DeleteAsync("/api/me/passkeys/anything");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, remove.StatusCode);
    }

    [Fact]
    public async Task A_new_account_has_no_passkeys()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("nokeys@example.com", "password1", "nokeys");

        var response = await client.GetAsync("/api/me/passkeys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadAsAsync<PasskeyDto[]>());
    }

    [Fact]
    public async Task Creation_options_name_the_signed_in_user()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("optme@example.com", "password1", "optme");

        var response = await client.PostAsync("/api/me/passkeys/creation-options", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var options = await response.ReadAsAsync<OptionsDto>();
        using var parsed = JsonDocument.Parse(options.OptionsJson);
        var user = parsed.RootElement.GetProperty("user");

        // What the authenticator shows in its account picker.
        Assert.Equal("optme@example.com", user.GetProperty("name").GetString());
        Assert.Equal("optme", user.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_malformed_credential_is_passkey_failed_not_a_500()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("badcred@example.com", "password1", "badcred");

        // Options first, so the failure is the credential and not a missing challenge.
        await client.PostAsync("/api/me/passkeys/creation-options", null);

        var response = await client.PostAsJsonAsync(
            "/api/me/passkeys", new { credentialJson = "{}", name = "Nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("passkey_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Registering_without_asking_for_options_is_passkey_failed()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("noopts@example.com", "password1", "noopts");

        // No creation-options call, so there is no challenge on file. Identity
        // throws in that case; the endpoint has to answer instead of 500ing.
        var response = await client.PostAsJsonAsync(
            "/api/me/passkeys", new { credentialJson = "{}", name = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("passkey_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Removing_an_unknown_or_malformed_id_is_404()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("rm@example.com", "password1", "rm-passkeys");

        // Well-formed base64url that belongs to nobody, and a string that is not
        // base64url at all. Both are 404 — a malformed id must not become a 500.
        var unknown = await client.DeleteAsync("/api/me/passkeys/AAECAwQF");
        var malformed = await client.DeleteAsync("/api/me/passkeys/not!valid!base64url");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("not_found", (await unknown.ReadApiErrorAsync()).Code);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }
}
