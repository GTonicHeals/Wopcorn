using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;

namespace Wopcorn.Server.Controllers;

[AllowAnonymous]
[Route("api/config")]
public class ConfigController : ApiControllerBase
{
    public record AttributionDto(string Text, string LogoUrl);

    public record ConfigResponse(
        string ImageBaseUrl,
        string[] PosterSizes,
        string[] BackdropSizes,
        string[] ProfileSizes,
        AttributionDto Attribution);

    // Hardcoded on purpose: these are stable, and this endpoint must answer with
    // TMDB down (FR-B8). FR-B9 requires the attribution to be rendered.
    private static readonly ConfigResponse Config = new(
        "https://image.tmdb.org/t/p/",
        ["w92", "w154", "w185", "w342", "w500", "w780", "original"],
        ["w300", "w780", "w1280", "original"],
        ["w45", "w185", "h632", "original"],
        new AttributionDto(
            "This product uses the TMDB API but is not endorsed or certified by TMDB.",
            "/tmdb-logo.svg"));

    [HttpGet]
    public IActionResult Get() => Ok(Config);
}
