using System.Net;
using System.Net.Http.Json;

namespace Wopcorn.Server.Tests;

/// <summary>
/// FR-A8: every route that is not explicitly anonymous answers 401 without a
/// cookie — and answers with the ApiError body, never an HTML login redirect.
/// </summary>
public class AuthorizationTests(WopcornApiFactory factory) : IClassFixture<WopcornApiFactory>
{
    public static TheoryData<string, string, bool> ProtectedRoutes => new()
    {
        // method, route, multipart body (the avatar endpoint only matches
        // multipart/form-data, so a JSON body is rejected at endpoint selection
        // before authorization ever runs).
        { "POST", "/api/auth/logout", false },
        { "PUT", "/api/me", false },
        { "PUT", "/api/me/avatar", true },
        { "DELETE", "/api/me/avatar", false },
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Protected_routes_answer_401_with_the_ApiError_shape(
        string method, string route, bool multipart)
    {
        using var client = factory.CreateAnonymousClient();

        HttpContent content = multipart
            ? new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.png" } }
            : JsonContent.Create(new { displayName = "anything" });

        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = content };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Not a 302/301 to a login page.
        Assert.Null(response.Headers.Location);

        var error = await response.ReadApiErrorAsync();
        Assert.Equal("unauthenticated", error.Code);
    }

    [Fact]
    public async Task Anonymous_routes_do_not_require_a_cookie()
    {
        using var client = factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/config")).StatusCode);
        // /api/auth/me is anonymous by design: it answers 401 from the action, not
        // from the authorization middleware, so the client can boot cleanly.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
