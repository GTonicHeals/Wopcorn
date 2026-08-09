using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Lists;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// Ratings (FR-E1..FR-E6). Separate from <see cref="TitlesController"/> so the
/// catalog controller stays read-only. Stars, halves and the 0.5–5.0 display are
/// the client's job — the server only ever sees an integer 1–10 (FR-E2), and that
/// is true of a series and a season exactly as it is of a film.
/// </summary>
[Route("api")]
public class RatingsController(
    ListService lists,
    RatingStatsService stats,
    TitleCacheService titles) : ApiControllerBase
{
    private const string RatingRangeMessage = "Rating must be between 1 and 10 half-stars.";

    public record RatingRequest(int? Rating);

    [HttpPut("titles/{key}/rating")]
    public async Task<IActionResult> Set(
        string key, RatingRequest request, CancellationToken ct = default)
    {
        if (request.Rating is not { } rating || rating is < 1 or > RatingStats.Buckets)
        {
            return BadRequest(new ApiError("validation_failed", RatingRangeMessage,
                new Dictionary<string, string[]> { ["rating"] = [RatingRangeMessage] }));
        }

        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        if (await EnsureTitleAsync(titles, parsed, ct) is { } failure)
        {
            return failure;
        }

        // FR-E3: implicitly adds to Watched when it is not there already.
        return Ok(await lists.SetRatingAsync(CurrentUserId, parsed, rating, ct));
    }

    [HttpDelete("titles/{key}/rating")]
    public async Task<IActionResult> Clear(string key, CancellationToken ct = default)
    {
        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        // FR-E4: the watched entry survives. Clearing an absent rating is still 204.
        await lists.ClearRatingAsync(CurrentUserId, parsed, ct);
        return NoContent();
    }

    [HttpGet("me/rating-stats")]
    public async Task<IActionResult> Stats(CancellationToken ct = default) =>
        // Same computation a friend's profile serves (be-04 task 3) — one query,
        // written once, with the user id always a parameter.
        Ok(await stats.ComputeAsync(CurrentUserId, ct));
}
