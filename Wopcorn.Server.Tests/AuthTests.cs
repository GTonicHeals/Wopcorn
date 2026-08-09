using System.Net;
using System.Net.Http.Json;

namespace Wopcorn.Server.Tests;

/// <summary>be-01 obligations: registration, the unique display name, and the
/// no-enumeration login response.</summary>
public class AuthTests(WopcornApiFactory factory) : IClassFixture<WopcornApiFactory>
{
    [Fact]
    public async Task Register_signs_the_caller_in_and_me_returns_them()
    {
        using var client = factory.CreateSessionClient();

        var registered = await client.RegisterAndReadAsync("ada@example.com", "password1", "ada");

        Assert.Equal("ada", registered.DisplayName);
        Assert.Null(registered.AvatarUrl);
        Assert.True(Guid.TryParse(registered.Id, out _));

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var current = await me.ReadAsAsync<UserSummaryDto>();
        Assert.Equal(registered.Id, current.Id);
        Assert.Equal("ada", current.DisplayName);
    }

    [Fact]
    public async Task Register_issues_a_persistent_cookie()
    {
        using var client = factory.CreateSessionClient();

        var response = await client.RegisterAsync("persist@example.com", "password1", "persist");

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("wopcorn.auth", StringComparison.Ordinal));
        // FR-A4: persistent, not a session cookie. NFR-5: https only.
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_display_name_is_rejected_with_409()
    {
        using var first = factory.CreateSessionClient();
        await first.RegisterAndReadAsync("first@example.com", "password1", "taken");

        using var second = factory.CreateAnonymousClient();
        var response = await second.RegisterAsync("second@example.com", "password1", "taken");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.ReadApiErrorAsync();
        Assert.Equal("display_name_taken", error.Code);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_answer_identically()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("known@example.com", "password1", "known");

        using var anonymous = factory.CreateAnonymousClient();

        var wrongPassword = await anonymous.LoginAsync("known@example.com", "not-the-password");
        var unknownEmail = await anonymous.LoginAsync("nobody@example.com", "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        var a = await wrongPassword.ReadApiErrorAsync();
        var b = await unknownEmail.ReadApiErrorAsync();

        // No user enumeration: identical code and identical message.
        Assert.Equal(a.Code, b.Code);
        Assert.Equal(a.Message, b.Message);
        Assert.Equal("unauthenticated", a.Code);
        Assert.Equal("Email or password is incorrect.", a.Message);
    }

    [Fact]
    public async Task Login_after_logout_restores_the_session()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("cycle@example.com", "password1", "cycle");

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        var login = await client.LoginAsync("cycle@example.com", "password1");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("cycle", (await me.ReadAsAsync<UserSummaryDto>()).DisplayName);
    }

    [Fact]
    public async Task Me_returns_401_for_an_anonymous_caller_without_a_redirect()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Rename_to_a_taken_display_name_is_409_and_to_its_own_name_succeeds()
    {
        using var owner = factory.CreateSessionClient();
        await owner.RegisterAndReadAsync("rename-a@example.com", "password1", "rename-a");

        using var other = factory.CreateSessionClient();
        await other.RegisterAndReadAsync("rename-b@example.com", "password1", "rename-b");

        var clash = await other.PutAsJsonAsync("/api/me", new { displayName = "rename-a" });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("display_name_taken", (await clash.ReadApiErrorAsync()).Code);

        var noop = await other.PutAsJsonAsync("/api/me", new { displayName = "rename-b" });
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.Equal("rename-b", (await noop.ReadAsAsync<UserSummaryDto>()).DisplayName);

        var renamed = await other.PutAsJsonAsync("/api/me", new { displayName = "rename-b2" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("rename-b2", (await renamed.ReadAsAsync<UserSummaryDto>()).DisplayName);
    }

    [Fact]
    public async Task Invalid_registration_uses_the_validation_failed_shape()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.RegisterAsync("not-an-email", "short", "x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.NotNull(error.Errors);
        Assert.NotEmpty(error.Errors);
    }
}
