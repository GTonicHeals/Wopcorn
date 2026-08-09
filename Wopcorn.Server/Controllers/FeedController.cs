using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Social;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The friends activity feed (FR-G2, FR-G3, FR-G7).
///
/// Friends only — your own activity is not news to you, and a non-friend's is
/// none of your business (FR-F6). Membership is resolved from
/// <c>Friendships</c> on every request, so unfriending someone takes their items
/// out of the next page you load (NFR-4).
///
/// FR-G7 needs no code here: be-03 deletes an <see cref="ActivityEvent"/> when the
/// action behind it is undone, so undone items simply stop being selected. No
/// tombstones, no soft deletes.
/// </summary>
[Route("api/feed")]
public class FeedController(
    WopcornDbContext db,
    FriendshipService friendships,
    TitleCacheService titles,
    TitleMapper mapper) : ApiControllerBase
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        FeedCursor? position = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!FeedCursor.TryParse(cursor, out var parsed))
            {
                // A hand-edited or truncated cursor is the client's problem to fix,
                // not a server fault.
                return BadRequest(new ApiError("validation_failed",
                    "That feed position is not one we recognise.",
                    new Dictionary<string, string[]> { ["cursor"] = ["Unrecognised cursor."] }));
            }

            position = parsed;
        }

        var friendIds = await friendships.GetFriendIdsAsync(CurrentUserId, ct);
        if (friendIds.Count == 0)
        {
            return Ok(new FeedPage([], null));
        }

        var rows = db.ActivityEvents
            .AsNoTracking()
            .Where(e => friendIds.Contains(e.UserId));

        if (position is { } from)
        {
            // (OccurredAt, Id) < (cursorTime, cursorId), spelled out because tuple
            // comparison is not something every provider translates. OccurredAt is
            // stored as UTC ticks and Id as its canonical text, so both the filter
            // and the ORDER BY below run in the database on the same ordering.
            var at = from.OccurredAt;
            var id = from.Id;
            rows = rows.Where(e => e.OccurredAt < at
                                   || (e.OccurredAt == at && e.Id.CompareTo(id) < 0));
        }

        // limit + 1 is how nextCursor is decided without a second COUNT query.
        var page = await rows
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(take + 1)
            .Include(e => e.User)
            .ToListAsync(ct);

        var hasMore = page.Count > take;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var keys = page.Select(e => e.TitleKey).Distinct(StringComparer.Ordinal).ToList();

        // One database read for the whole page and no upstream calls: the feed
        // renders during a TMDB outage (FR-B6, NFR-2).
        var titleRows = await titles.GetManyAsync(keys, ct);
        var context = await mapper.LoadUserContextAsync(CurrentUserId, keys, ct);

        var items = page
            .Where(e => titleRows.ContainsKey(e.TitleKey))
            .Select(e => new ActivityItem(
                e.Id.ToString(),
                e.User.ToSummary(),
                e.Kind.ToWire(),
                mapper.ToCard(titleRows[e.TitleKey], context),
                e.Kind == ActivityKind.Rated ? e.Rating : null,
                e.OccurredAt))
            .ToArray();

        var nextCursor = hasMore && page.Count > 0
            ? new FeedCursor(page[^1].OccurredAt, page[^1].Id).Encode()
            : null;

        return Ok(new FeedPage(items, nextCursor));
    }
}
