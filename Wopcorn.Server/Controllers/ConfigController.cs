using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;

namespace Wopcorn.Server.Controllers;

[AllowAnonymous]
[Route("api/config")]
public class ConfigController : ApiControllerBase
{
    /// <param name="AvailabilityText">
    /// The attribution the streaming data comes with (plan 09), to be rendered
    /// wherever availability is. Text only — there is no second logo file, because
    /// <c>wwwroot/tmdb-logo.svg</c> has been outstanding since FR-B9 and one
    /// unshipped trademarked asset is enough.
    /// </param>
    public record AttributionDto(string Text, string LogoUrl, string AvailabilityText);

    public record ConfigResponse(
        string ImageBaseUrl,
        string[] PosterSizes,
        string[] BackdropSizes,
        string[] ProfileSizes,
        string[] LogoSizes,
        AttributionDto Attribution);

    // Hardcoded on purpose: these are stable, and this endpoint must answer with
    // TMDB down (FR-B8). FR-B9 requires the attribution to be rendered.
    private static readonly ConfigResponse Config = new(
        "https://image.tmdb.org/t/p/",
        ["w92", "w154", "w185", "w342", "w500", "w780", "original"],
        ["w300", "w780", "w1280", "original"],
        ["w45", "w185", "h632", "original"],
        ["w45", "w92", "w154", "w185", "original"],
        new AttributionDto(
            "This product uses the TMDB API but is not endorsed or certified by TMDB.",
            "/tmdb-logo.svg",
            "Streaming availability data provided by JustWatch."));

    [HttpGet]
    public IActionResult Get() => Ok(Config);
}
