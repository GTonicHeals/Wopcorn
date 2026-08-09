using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Lists;

/// <summary>
/// FR-E6, in one grouped query. Extracted from <see cref="ListService"/> because
/// two endpoints need it for two different people — <c>GET /api/me/rating-stats</c>
/// for the caller and <c>GET /api/friends/{userId}/profile</c> for a friend — and
/// the query must not be written twice.
///
/// The user id is a <b>parameter</b>: this type never reads the session, so the
/// only way to reach someone else's stats is a caller that has already verified
/// the friendship (NFR-3, NFR-4).
/// </summary>
public sealed class RatingStatsService(WopcornDbContext db)
{
    public async Task<RatingStats> ComputeAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.Kind == ListKind.Watched && e.Rating != null)
            .GroupBy(e => e.Rating!.Value)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return RatingStats.From(rows.Select(r => (r.Rating, r.Count)));
    }

    /// <summary>
    /// The entry count per list, for a profile header (FR-G1). One grouped query,
    /// so three counts cost one round trip.
    /// </summary>
    public async Task<ListCounts> CountsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byKind = rows.ToDictionary(r => r.Kind, r => r.Count);

        return new ListCounts(
            byKind.GetValueOrDefault(ListKind.Watched),
            byKind.GetValueOrDefault(ListKind.Watchlist),
            byKind.GetValueOrDefault(ListKind.Queue));
    }
}
