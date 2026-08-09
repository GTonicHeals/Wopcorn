using System.Net;
using System.Net.Http.Headers;

namespace Wopcorn.Server.Tests;

/// <summary>FR-A7: avatar upload limits.</summary>
public class AvatarTests(WopcornApiFactory factory) : IClassFixture<WopcornApiFactory>
{
    private record AvatarResponseDto(string? AvatarUrl);

    private static MultipartFormDataContent FileContent(byte[] bytes, string contentType, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Non_image_content_type_is_rejected()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("avatar-type@example.com", "password1", "avatar-type");

        using var content = FileContent("not an image"u8.ToArray(), "text/plain", "payload.txt");
        var response = await client.PutAsync("/api/me/avatar", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task An_image_over_2_MB_is_rejected()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("avatar-size@example.com", "password1", "avatar-size");

        var oversized = new byte[(2 * 1024 * 1024) + 1];
        using var content = FileContent(oversized, "image/png", "big.png");
        var response = await client.PutAsync("/api/me/avatar", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_valid_upload_is_served_and_can_be_removed()
    {
        using var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("avatar-ok@example.com", "password1", "avatar-ok");

        // Smallest possible valid PNG payload; the endpoint checks type and size,
        // not pixels.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var content = FileContent(png, "image/png", "../../evil.png");
        var upload = await client.PutAsync("/api/me/avatar", content);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.ReadAsAsync<AvatarResponseDto>();
        Assert.NotNull(uploaded.AvatarUrl);
        // The client filename is never used.
        Assert.StartsWith("/avatars/", uploaded.AvatarUrl);
        Assert.DoesNotContain("evil", uploaded.AvatarUrl);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(uploaded.AvatarUrl, (await me.ReadAsAsync<UserSummaryDto>()).AvatarUrl);

        var served = await client.GetAsync(uploaded.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        var deleted = await client.DeleteAsync("/api/me/avatar");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = await client.GetAsync("/api/auth/me");
        Assert.Null((await after.ReadAsAsync<UserSummaryDto>()).AvatarUrl);
    }
}
