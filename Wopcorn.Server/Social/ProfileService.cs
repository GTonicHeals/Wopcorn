using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Lists;

namespace Wopcorn.Server.Social;

/// <summary>
/// Builds the one <see cref="ProfileDto"/> that backs both
/// <c>GET /api/me/profile</c> and <c>GET /api/friends/{userId}/profile</c>.
///
/// The owner and the viewer are separate parameters, and neither is read from the
/// session here (NFR-3): a caller that has not already established it may look at
/// this owner cannot reach this type. The friendship check stays in
/// <see cref="FriendshipService"/>, in front of the controller action.
///
/// Every query below is a constant, whatever the size of the account — a profile
/// with 2,000 watched titles costs the same eight round trips as an empty one
/// (NFR-2).
/// </summary>
public sealed class ProfileService(
    WopcornDbContext db,
    RatingStatsService stats,
    FavoritesService favorites,
    TitleMapper mapper)
{
    /// <summary>Five is a legible column of bars; a genre tail is not a taste.</summary>
    private const int TopGenreCount = 5;

    /// <summary>Enough to say what someone has been up to, short of being a feed.</summary>
    private const int RecentActivityCount = 8;

    public async Task<ProfileDto> BuildAsync(
        AppUser owner, Guid viewerId, TasteMatch? tasteMatch, CancellationToken ct)
    {
        var ownerId = owner.Id;

        return new ProfileDto(
            owner.ToSummary(),
            ownerId == viewerId,
            owner.CreatedAt,
            await stats.ComputeAsync(ownerId, ct),
            await stats.CountsAsync(ownerId, ct),
            await favorites.GetAsync(ownerId, viewerId, ct),
            await TopGenresAsync(ownerId, ct),
            await RuntimeAsync(ownerId, ct),
            await FriendCountAsync(ownerId, ct),
            await RecentActivityAsync(ownerId, viewerId, ct),
            tasteMatch);
    }

    /// <summary>
    /// The genres of the watched list, counted in the database. A season counts as
    /// itself: its genres are its series' genres, so watching four seasons of one
    /// show does say something about taste, four times over.
    /// </summary>
    private async Task<IReadOnlyList<GenreAffinity>> TopGenresAsync(
        Guid ownerId, CancellationToken ct)
    {
        var rows = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == ownerId && e.Kind == ListKind.Watched)
            .SelectMany(e => e.Title.Genres)
            .GroupBy(g => new { g.GenreTmdbId, g.Genre.Name })
            .Select(g => new { g.Key.GenreTmdbId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name)
            .Take(TopGenreCount)
            .ToListAsync(ct);

        return rows.Select(r => new GenreAffinity(r.GenreTmdbId, r.Name, r.Count)).ToArray();
    }

    /// <summary>
    /// Watched runtime, with the unknown half counted rather than hidden. One
    /// aggregate query; the null runtimes never reach memory as rows.
    /// </summary>
    private async Task<RuntimeOnRecord> RuntimeAsync(Guid ownerId, CancellationToken ct)
    {
        var watched = db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == ownerId && e.Kind == ListKind.Watched);

        var totals = await watched
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Minutes = g.Sum(e => e.Title.RuntimeMinutes ?? 0),
                Known = g.Count(e => e.Title.RuntimeMinutes != null && e.Title.RuntimeMinutes > 0),
                Total = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return totals is null
            ? new RuntimeOnRecord(0, 0, 0)
            : new RuntimeOnRecord(totals.Minutes, totals.Known, totals.Total - totals.Known);
    }

    private Task<int> FriendCountAsync(Guid ownerId, CancellationToken ct) =>
        db.Friendships
            .AsNoTracking()
            .CountAsync(f => f.UserAId == ownerId || f.UserBId == ownerId, ct);

    /// <summary>
    /// The owner's newest events, decorated for the <b>viewer</b> — so a friend's
    /// "watched Heat" card still shows your toggles and your rating, exactly as it
    /// does in the feed and on their lists.
    /// </summary>
    private async Task<IReadOnlyList<ActivityItem>> RecentActivityAsync(
        Guid ownerId, Guid viewerId, CancellationToken ct)
    {
        var rows = await db.ActivityEvents
            .AsNoTracking()
            .Where(e => e.UserId == ownerId)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(RecentActivityCount)
            .Include(e => e.User)
            .Include(e => e.Title)
            .ThenInclude(t => t.Genres)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var context = await mapper.LoadUserContextAsync(
            viewerId, rows.Select(r => r.TitleKey).Distinct(StringComparer.Ordinal).ToList(), ct);

        return rows
            .Select(e => new ActivityItem(
                e.Id.ToString(),
                e.User.ToSummary(),
                e.Kind.ToWire(),
                mapper.ToCard(e.Title, context),
                e.Kind == ActivityKind.Rated ? e.Rating : null,
                e.OccurredAt))
            .ToArray();
    }
}
