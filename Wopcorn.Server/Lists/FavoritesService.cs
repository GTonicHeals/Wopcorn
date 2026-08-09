using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Lists;

/// <summary>
/// The favourites showcase (API-CONTRACT.md, "Profile and favourites").
///
/// A showcase is written as a whole, exactly like the queue's order: the body of
/// <c>PUT /api/me/favorites</c> is the complete intended list, so add, remove and
/// reorder are one operation and there is no per-slot state that can drift.
///
/// The owner is always a parameter and never read from the session here, which is
/// what keeps the read path usable for a friend's profile without opening a hole
/// on the write path — only the controller can name the writer (NFR-3).
/// </summary>
public sealed class FavoritesService(WopcornDbContext db, TitleMapper mapper)
{
    /// <summary>
    /// Six slots. The number is a design decision, not a storage one: the showcase
    /// is a single row of posters on the profile, and a row that wraps stops being
    /// a showcase and starts being another list.
    /// </summary>
    public const int MaxFavorites = 6;

    /// <summary>
    /// The showcase in stored order, decorated for <paramref name="viewerId"/> —
    /// so a friend's favourites carry <b>your</b> list toggles and rating, the same
    /// split every other borrowed row uses.
    /// </summary>
    public async Task<IReadOnlyList<TitleCard>> GetAsync(
        Guid ownerId, Guid viewerId, CancellationToken ct)
    {
        var rows = await db.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == ownerId)
            .OrderBy(f => f.Position)
            .Include(f => f.Title)
            .ThenInclude(t => t.Genres)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var context = await mapper.LoadUserContextAsync(
            viewerId, rows.Select(r => r.TitleKey).ToList(), ct);

        return rows.Select(r => mapper.ToCard(r.Title, context)).ToArray();
    }

    /// <summary>
    /// Replaces the whole showcase. Validation happens before anything is deleted,
    /// so a rejected write leaves the existing showcase exactly as it was.
    /// </summary>
    /// <exception cref="ApiException">
    /// <c>400 validation_failed</c> for a malformed key, a repeat, or more than
    /// <see cref="MaxFavorites"/>; <c>404 not_found</c> for a title the local
    /// catalog does not hold. Favouriting never reaches TMDB.
    /// </exception>
    public async Task<IReadOnlyList<TitleCard>> ReplaceAsync(
        Guid userId, IReadOnlyList<string>? keys, CancellationToken ct)
    {
        var wanted = Validate(keys);

        var known = await db.Titles
            .AsNoTracking()
            .Where(t => wanted.Contains(t.Key))
            .Select(t => t.Key)
            .ToListAsync(ct);

        var missing = wanted.Except(known, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "One of those titles is not one we know about yet.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.Favorites.Where(f => f.UserId == userId).ToListAsync(ct);
        db.Favorites.RemoveRange(existing);

        // Flushed before the inserts: the unique (UserId, TitleKey) index would
        // otherwise see the old rows and the new ones at once.
        await db.SaveChangesAsync(ct);

        for (var i = 0; i < wanted.Count; i++)
        {
            db.Favorites.Add(new FavoriteTitle
            {
                UserId = userId,
                TitleKey = wanted[i],
                Position = i,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(userId, userId, ct);
    }

    /// <summary>
    /// The keys, canonicalised. Every failure here is the caller's mistake and is
    /// reported before a single row is touched.
    /// </summary>
    private static List<string> Validate(IReadOnlyList<string>? keys)
    {
        var supplied = keys ?? [];

        if (supplied.Count > MaxFavorites)
        {
            throw Invalid($"A showcase holds up to {MaxFavorites} titles.");
        }

        var parsed = new List<string>(supplied.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in supplied)
        {
            if (!TitleKey.TryParse(key, out var titleKey))
            {
                // Consistent with every other route that takes a key: a malformed
                // identifier is a bad request, never a 404.
                throw Invalid("Expected movie-123, tv-123, or tv-123-s1.");
            }

            if (!seen.Add(titleKey.Value))
            {
                throw Invalid("A title can only hold one slot in the showcase.");
            }

            parsed.Add(titleKey.Value);
        }

        return parsed;
    }

    private static ApiException Invalid(string detail) =>
        new(StatusCodes.Status400BadRequest, "validation_failed",
            "Those favourites could not be saved.",
            new Dictionary<string, string[]> { ["keys"] = [detail] });
}
