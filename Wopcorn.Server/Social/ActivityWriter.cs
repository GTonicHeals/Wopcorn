using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Social;

/// <summary>
/// Writes and retracts the feed's raw material (FR-G7). be-03 is the only writer;
/// be-04 is the only reader. Nothing here is exposed over HTTP.
///
/// Every method mutates the change tracker only — the caller's
/// <c>SaveChanges</c> inside its own transaction is what makes the event and the
/// mutation it describes atomic. That is the whole point: an event must never
/// outlive the action it reports.
/// </summary>
public sealed class ActivityWriter(WopcornDbContext db)
{
    /// <summary>The event a plain "added to this list" produces.</summary>
    public static ActivityKind KindFor(ListKind kind) => kind switch
    {
        ListKind.Watched => ActivityKind.Watched,
        ListKind.Watchlist => ActivityKind.AddedWatchlist,
        ListKind.Queue => ActivityKind.AddedQueue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Records an event, <b>replacing</b> any existing event of the same kind for
    /// the same (user, title). A re-rate therefore moves up the feed instead of
    /// appearing twice (task 6).
    /// </summary>
    public async Task RecordAsync(
        Guid userId, TitleKey key, ActivityKind kind, int? rating, CancellationToken ct)
    {
        await RetractAsync(userId, key, kind, ct);

        db.ActivityEvents.Add(new ActivityEvent
        {
            UserId = userId,
            TitleKey = key.Value,
            Kind = kind,
            Rating = rating,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Undo: the feed item for this (user, title, kind) disappears.</summary>
    public async Task RetractAsync(
        Guid userId, TitleKey key, ActivityKind kind, CancellationToken ct)
    {
        // Tracked delete rather than ExecuteDelete: ExecuteDelete runs immediately
        // and bypasses the change tracker, which would fight the pending insert in
        // RecordAsync. The set is at most one row.
        var value = key.Value;
        var existing = await db.ActivityEvents
            .Where(a => a.UserId == userId && a.TitleKey == value && a.Kind == kind)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            db.ActivityEvents.RemoveRange(existing);
        }
    }
}
