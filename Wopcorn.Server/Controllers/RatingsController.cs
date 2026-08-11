using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;
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

    public record CommentRequest(string? Comment);

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

    /// <summary>
    /// Plan 10. A note lives here rather than in a controller of its own because it
    /// is a rating with words: same row, same implicit add to Watched, same
    /// survival rules when it is cleared. Two controllers would be two places to
    /// keep those rules in step.
    /// </summary>
    [HttpPut("titles/{key}/comment")]
    public async Task<IActionResult> SetComment(
        string key, CommentRequest request, CancellationToken ct = default)
    {
        if (!SuggestionsController.TryNormalizeComment(
                request.Comment, ListEntry.MaxCommentLength, out var comment))
        {
            return CommentError($"A note is at most {ListEntry.MaxCommentLength} characters.");
        }

        // Blank is a bad request rather than a silent clear: DELETE is how you take
        // a note back, and a PUT that quietly deleted would be a surprising way to
        // lose one to a stray keystroke.
        if (comment is null)
        {
            return CommentError("Write something, or delete the note instead.");
        }

        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        if (await EnsureTitleAsync(titles, parsed, ct) is { } failure)
        {
            return failure;
        }

        // Implicitly adds to Watched, exactly as rating does (FR-E3).
        return Ok(await lists.SetCommentAsync(CurrentUserId, parsed, comment, ct));
    }

    [HttpDelete("titles/{key}/comment")]
    public async Task<IActionResult> ClearComment(string key, CancellationToken ct = default)
    {
        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        // The watched entry survives, like DELETE .../rating. Clearing an absent
        // note is still 204.
        await lists.ClearCommentAsync(CurrentUserId, parsed, ct);
        return NoContent();
    }

    private IActionResult CommentError(string message) =>
        BadRequest(new ApiError("validation_failed", message,
            new Dictionary<string, string[]> { ["comment"] = [message] }));

    [HttpGet("me/rating-stats")]
    public async Task<IActionResult> Stats(CancellationToken ct = default) =>
        // Same computation a friend's profile serves (be-04 task 3) — one query,
        // written once, with the user id always a parameter.
        Ok(await stats.ComputeAsync(CurrentUserId, ct));
}
