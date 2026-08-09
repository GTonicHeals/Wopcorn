using System.Net;

namespace Wopcorn.Server.Tests;

public class ConfigTests(WopcornApiFactory factory) : IClassFixture<WopcornApiFactory>
{
    private record AttributionDto(string Text, string LogoUrl);

    private record ConfigDto(
        string ImageBaseUrl,
        string[] PosterSizes,
        string[] BackdropSizes,
        string[] ProfileSizes,
        AttributionDto Attribution);

    [Fact]
    public async Task Config_answers_anonymously_with_the_contract_shape()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.ReadAsAsync<ConfigDto>();

        Assert.Equal("https://image.tmdb.org/t/p/", config.ImageBaseUrl);
        Assert.Equal(["w92", "w154", "w185", "w342", "w500", "w780", "original"], config.PosterSizes);
        Assert.Equal(["w300", "w780", "w1280", "original"], config.BackdropSizes);
        Assert.Equal(["w45", "w185", "h632", "original"], config.ProfileSizes);
        Assert.Equal(
            "This product uses the TMDB API but is not endorsed or certified by TMDB.",
            config.Attribution.Text);   // FR-B9
        Assert.Equal("/tmdb-logo.svg", config.Attribution.LogoUrl);
    }
}
