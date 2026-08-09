using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

[ApiController]
[Authorize]
[Route("api")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);   // NFR-3

    protected IActionResult Problem(int status, string code, string message) =>
        StatusCode(status, new ApiError(code, message));

    /// <summary>
    /// Parses a <c>{key}</c> route segment, or produces the failure to return.
    ///
    /// A malformed key is <c>400 validation_failed</c> and never <c>404</c>: the
    /// caller did not name a title that is missing, they sent something that is not
    /// an identifier at all. Every route that takes a key uses this, so the
    /// distinction is made once.
    /// </summary>
    protected bool TryParseKey(string? key, out TitleKey parsed, out IActionResult? failure)
    {
        if (TitleKey.TryParse(key, out parsed))
        {
            failure = null;
            return true;
        }

        failure = BadRequest(new ApiError("validation_failed", "That is not a title identifier.",
            new Dictionary<string, string[]>
            {
                ["key"] = ["Expected movie-123, tv-123, or tv-123-s1."],
            }));
        return false;
    }

    /// <summary>
    /// A write may only reference a title the cache holds, which is what keeps list
    /// rendering off the network entirely (FR-B6). <c>NotFound</c> is a genuine 404;
    /// a missing title with TMDB unreachable is a 503.
    /// </summary>
    protected async Task<IActionResult?> EnsureTitleAsync(
        TitleCacheService titles, TitleKey key, CancellationToken ct)
    {
        var result = await titles.GetDetailAsync(key, forceRefresh: false, ct);
        if (result.Title is not null)
        {
            return null;
        }

        return result.NotFound
            ? Problem(404, "not_found", "We could not find that title.")
            : Problem(503, "tmdb_unavailable", TmdbUnavailableFilter.Message);
    }
}
