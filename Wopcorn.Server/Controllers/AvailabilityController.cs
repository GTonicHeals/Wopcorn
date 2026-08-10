using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The Streaming availability section of API-CONTRACT.md (plan 09).
///
/// Both routes read the region from the <b>authenticated user</b> and never from a
/// query parameter (NFR-3): availability with no region is not approximate, it is
/// wrong, so region is state rather than an argument.
///
/// Nothing here answers <c>503</c>. Availability decorates a page and must never
/// fail one — <see cref="AvailabilityService"/> degrades an outage to stale rows or
/// to "unknown" on its own.
/// </summary>
[Route("api")]
public class AvailabilityController(
    AvailabilityService availability,
    TitleCacheService titles) : ApiControllerBase
{
    [HttpGet("titles/{key}/availability")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        if (await RegionAsync(ct) is not { } region)
        {
            return NoRegion();
        }

        // A season's providers are its series', and it is the series row the
        // availability table foreign-keys to — so the parent is what has to exist.
        var resolved = AvailabilityService.Resolve(parsed);
        if (await EnsureTitleAsync(titles, resolved, ct) is { } failure)
        {
            return failure;
        }

        var snapshot = await availability.GetAsync(resolved, region, ct);
        return Ok(TitleAvailabilityDto.From(snapshot));
    }

    /// <summary>
    /// The services TMDB publishes for the viewer's region, ordered by TMDB's own
    /// <c>display_priority</c> — so the handful someone might actually subscribe to
    /// are at the top and the long tail is below them.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> Directory(CancellationToken ct)
    {
        if (await RegionAsync(ct) is not { } region)
        {
            return NoRegion();
        }

        var providers = await availability.EnsureDirectoryAsync(region, ct);
        return Ok(providers.Select(WatchProviderDto.From).ToArray());
    }

    private async Task<string?> RegionAsync(CancellationToken ct) =>
        (await availability.ViewerAsync(CurrentUserId, ct)).Region;

    /// <summary>
    /// <c>errors.region</c> is what lets the client route to settings rather than
    /// render a dead block (NFR-10).
    /// </summary>
    private IActionResult NoRegion() =>
        BadRequest(new ApiError("validation_failed", "Choose where you watch first.",
            new Dictionary<string, string[]>
            {
                ["region"] = ["Set your region in settings to see streaming availability."],
            }));
}
