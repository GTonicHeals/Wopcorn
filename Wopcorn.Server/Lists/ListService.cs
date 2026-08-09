using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Social;

namespace Wopcorn.Server.Lists;

/// <summary>
/// The query string of <c>GET /api/lists/{list}</c> (FR-C4, FR-C5). Unknown
/// values are ignored here, never rejected — a stale bookmark must still render.
/// </summary>
public record ListQuery(
    string? Sort, string? Dir, int[] GenreIds, int[] Decades, MediaType[] Types)
{
    public static readonly ListQuery None = new(null, null, [], [], []);
}

/// <summary>
/// Every list mutation goes through this type; controllers only validate and map.
/// The acting user is always passed in from <c>CurrentUserId</c> — no method here
/// can be reached with a user id that came off the wire (NFR-3).
/// </summary>
public sealed class ListService(
    WopcornDbContext db,
    TitleMapper mapper,
    ActivityWriter activity,
    TasteMatchInvalidator tasteMatch)
{
    /// <summary>
    /// Idempotent add (FR-C1..FR-C3). A repeat add returns the existing entry with
    /// its original <c>AddedAt</c> and emits no new activity; only a supplied
    /// <paramref name="watchedOn"/> is written through.
    /// </summary>
    public async Task<ListEntryDto> AddAsync(
        Guid userId,
        TitleKey key,
        ListKind kind,
        IReadOnlyCollection<ListKind> alsoRemoveFrom,
        DateOnly? watchedOn,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FR-C6, in one round trip. Done first so a queue removal has compacted
        // before the next position is computed.
        foreach (var other in alsoRemoveFrom.Distinct().Where(k => k != kind))
        {
            await RemoveCoreAsync(userId, key, other, ct);
        }

        var entry = await FindAsync(userId, key, kind, ct);

        if (entry is null)
        {
            entry = new ListEntry
            {
                UserId = userId,
                TitleKey = key.Value,
                Kind = kind,
                AddedAt = DateTimeOffset.UtcNow,
                // FR-D1: a new queue entry appends to the end.
                Position = kind == ListKind.Queue ? await NextQueuePositionAsync(userId, ct) : null,
                WatchedOn = kind == ListKind.Watched ? watchedOn : null,
            };

            db.ListEntries.Add(entry);
            await activity.RecordAsync(userId, key, ActivityWriter.KindFor(kind), null, ct);
        }
        else if (kind == ListKind.Watched && watchedOn is not null)
        {
            // The one field a repeat add may change. AddedAt is never bumped.
            entry.WatchedOn = watchedOn;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await ProjectAsync(entry, userId, ct);
    }

    /// <summary>
    /// Idempotent remove — an absent entry is a no-op, so the controller answers
    /// <c>204</c> either way.
    /// </summary>
    public async Task RemoveAsync(Guid userId, TitleKey key, ListKind kind, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RemoveCoreAsync(userId, key, kind, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// FR-E3: rating a title is watching it. Creates the Watched entry when it is
    /// missing, then stores the rating. Both the implied <c>Watched</c> event and
    /// the <c>Rated</c> event land in the same transaction as the row.
    /// </summary>
    public async Task<ListEntryDto> SetRatingAsync(
        Guid userId, TitleKey key, int rating, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var entry = await FindAsync(userId, key, ListKind.Watched, ct);

        if (entry is null)
        {
            entry = new ListEntry
            {
                UserId = userId,
                TitleKey = key.Value,
                Kind = ListKind.Watched,
                AddedAt = DateTimeOffset.UtcNow,
            };

            db.ListEntries.Add(entry);
            await activity.RecordAsync(userId, key, ActivityKind.Watched, null, ct);
        }

        entry.Rating = rating;
        await activity.RecordAsync(userId, key, ActivityKind.Rated, rating, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // be-04 task 4: this user's cached taste matches are now wrong.
        tasteMatch.Invalidate(userId);

        return await ProjectAsync(entry, userId, ct);
    }

    /// <summary>
    /// FR-E4: clearing a rating keeps the Watched entry. Clearing an absent rating
    /// is a no-op.
    /// </summary>
    public async Task ClearRatingAsync(Guid userId, TitleKey key, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var entry = await FindAsync(userId, key, ListKind.Watched, ct);
        if (entry is not null)
        {
            entry.Rating = null;
        }

        await activity.RetractAsync(userId, key, ActivityKind.Rated, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        tasteMatch.Invalidate(userId);
    }

    /// <summary>
    /// The unfiltered size of the list, for the "42 titles" header beside a filtered
    /// result (FR-C4). Unfiltered includes the media-type filter: "showing 12 of 84"
    /// needs a denominator the type checkboxes do not move.
    /// </summary>
    public Task<int> CountAsync(Guid userId, ListKind kind, CancellationToken ct) =>
        db.ListEntries.CountAsync(e => e.UserId == userId && e.Kind == kind, ct);

    /// <summary>
    /// A whole list view in a constant number of queries regardless of its size
    /// (NFR-2): the entries with their titles, then one decoration query for the
    /// page.
    /// </summary>
    /// <param name="ownerId">Whose rows are listed.</param>
    /// <param name="viewerId">
    /// Whose <c>lists</c> and <c>myRating</c> decorate them. The two differ on
    /// <c>GET /api/friends/{userId}/lists/{list}</c>: the contract says
    /// <c>TitleCard.lists</c> is "for the authenticated user", so a friend's list
    /// renders their rows with <b>my</b> memberships — which is what lets "my
    /// friend loved this, and it's already on my watchlist" appear in one pass
    /// (be-04 task 3). The friend's own rating rides on
    /// <see cref="ListEntryDto.Rating"/>.
    /// </param>
    public async Task<IReadOnlyList<ListEntryDto>> GetAsync(
        Guid ownerId, Guid viewerId, ListKind kind, ListQuery query, CancellationToken ct)
    {
        var rows = db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == ownerId && e.Kind == kind);

        rows = ApplyFilters(rows, query);
        rows = kind == ListKind.Queue
            // FR-D1: the queue's order is its stored order. Its sort presets are a
            // write (POST /api/queue/sort), not a view.
            ? ByStoredPosition(rows)
            : ApplyOrder(rows, query, ratingAllowed: kind == ListKind.Watched);

        var entries = await rows
            .Include(e => e.Title)
            .ThenInclude(t => t.Genres)
            .ToListAsync(ct);

        var context = await mapper.LoadUserContextAsync(
            viewerId, entries.Select(e => e.TitleKey).ToList(), ct);

        return entries.Select(e => ToDto(e, e.Title, context)).ToArray();
    }

    /// <summary>The stored queue order, as keys.</summary>
    public async Task<IReadOnlyList<string>> GetQueueOrderAsync(Guid userId, CancellationToken ct) =>
        await ByStoredPosition(db.ListEntries.AsNoTracking().Where(QueueOf(userId)))
            .Select(e => e.TitleKey)
            .ToListAsync(ct);

    /// <summary>
    /// FR-D2/FR-D5. <paramref name="keys"/> must be exactly the stored queue — same
    /// members, no duplicates, nothing missing. Anything else is
    /// <c>queue_out_of_sync</c> and the caller gets the authoritative order back
    /// instead of a corrupted one.
    ///
    /// The queue mixes media types freely: a film, a series and a season are three
    /// keys in one order and reorder against each other like any other three.
    /// </summary>
    public async Task<(bool Ok, IReadOnlyList<string> Keys)> ReorderQueueAsync(
        Guid userId, IReadOnlyList<string> keys, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var entries = await db.ListEntries.Where(QueueOf(userId)).ToListAsync(ct);
        var stored = entries.Select(e => e.TitleKey).ToHashSet(StringComparer.Ordinal);

        if (keys.Count != stored.Count ||
            keys.Distinct(StringComparer.Ordinal).Count() != keys.Count ||
            !keys.All(stored.Contains))
        {
            return (false, InStoredOrder(entries));
        }

        var byKey = entries.ToDictionary(e => e.TitleKey, StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            byKey[keys[i]].Position = i;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, keys);
    }

    /// <summary>
    /// FR-D3. A preset is a <b>write</b>: it rewrites stored positions and returns
    /// the result. Positions stay plain integers afterwards, so the queue is still
    /// hand-draggable (FR-D4).
    /// </summary>
    public async Task<IReadOnlyList<string>> SortQueueAsync(
        Guid userId, string? preset, string? dir, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Same ordering rules as the list view, including nulls-last. `rating` is not
        // a queue preset, so it falls back to `added`.
        var entries = await ApplyOrder(
                db.ListEntries.Where(QueueOf(userId)),
                new ListQuery(preset, dir, [], [], []),
                ratingAllowed: false)
            .ToListAsync(ct);

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Position = i;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return entries.Select(e => e.TitleKey).ToArray();
    }

    // --- mutation internals -------------------------------------------------

    /// <summary>
    /// Deletes one entry and retracts what it produced, without opening a
    /// transaction of its own — every caller already holds one.
    /// </summary>
    private async Task RemoveCoreAsync(
        Guid userId, TitleKey key, ListKind kind, CancellationToken ct)
    {
        var entry = await FindAsync(userId, key, kind, ct);
        if (entry is null)
        {
            return;
        }

        db.ListEntries.Remove(entry);
        await activity.RetractAsync(userId, key, ActivityWriter.KindFor(kind), ct);

        if (kind == ListKind.Watched)
        {
            // The rating lived on this row, so it is discarded with it — and its feed
            // item has to go too (FR-G7).
            await activity.RetractAsync(userId, key, ActivityKind.Rated, ct);
            tasteMatch.Invalidate(userId);
        }

        await db.SaveChangesAsync(ct);

        if (kind == ListKind.Queue)
        {
            await CompactQueueAsync(userId, ct);
        }
    }

    /// <summary>Keeps queue positions contiguous from 0 after a removal (FR-D1).</summary>
    private async Task CompactQueueAsync(Guid userId, CancellationToken ct)
    {
        var queue = await ByStoredPosition(db.ListEntries.Where(QueueOf(userId))).ToListAsync(ct);

        var changed = false;
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i].Position != i)
            {
                queue[i].Position = i;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> NextQueuePositionAsync(Guid userId, CancellationToken ct)
    {
        var max = await db.ListEntries.Where(QueueOf(userId)).MaxAsync(e => e.Position, ct);
        return (max ?? -1) + 1;
    }

    private Task<ListEntry?> FindAsync(
        Guid userId, TitleKey key, ListKind kind, CancellationToken ct)
    {
        var value = key.Value;
        return db.ListEntries.FirstOrDefaultAsync(
            e => e.UserId == userId && e.TitleKey == value && e.Kind == kind, ct);
    }

    // --- projection ---------------------------------------------------------

    private async Task<ListEntryDto> ProjectAsync(ListEntry entry, Guid userId, CancellationToken ct)
    {
        var title = await db.Titles
            .AsNoTracking()
            .Include(t => t.Genres)
            .FirstAsync(t => t.Key == entry.TitleKey, ct);

        var context = await mapper.LoadUserContextAsync(userId, [entry.TitleKey], ct);
        return ToDto(entry, title, context);
    }

    private ListEntryDto ToDto(
        ListEntry entry,
        Title title,
        IReadOnlyDictionary<string, TitleMapper.UserContext> context) =>
        new(mapper.ToCard(title, context),
            entry.AddedAt,
            entry.Position,
            entry.WatchedOn?.ToString("yyyy-MM-dd"),
            entry.Rating);

    // --- ordering and filtering (task 2) ------------------------------------

    private static Expression<Func<ListEntry, bool>> QueueOf(Guid userId) =>
        e => e.UserId == userId && e.Kind == ListKind.Queue;

    private static IQueryable<ListEntry> ApplyFilters(IQueryable<ListEntry> rows, ListQuery query)
    {
        if (query.Types.Length > 0)
        {
            // Applied in the database beside genre and decade, not in memory — the
            // whole point of the filter is that a long list does not have to be
            // materialised to narrow it.
            var types = query.Types;
            rows = rows.Where(e => types.Contains(e.Title.MediaType));
        }

        if (query.GenreIds.Length > 0)
        {
            var genreIds = query.GenreIds;
            rows = rows.Where(e => e.Title.Genres.Any(g => genreIds.Contains(g.GenreTmdbId)));
        }

        if (query.Decades.Length > 0)
        {
            // Decade start year. Integer division translates on every relational
            // provider we care about; nothing here is SQLite-specific (NFR-7).
            var decades = query.Decades;
            rows = rows.Where(e => e.Title.ReleaseDate != null
                                   && decades.Contains(e.Title.ReleaseDate.Value.Year / 10 * 10));
        }

        return rows;
    }

    private static IOrderedQueryable<ListEntry> ByStoredPosition(IQueryable<ListEntry> rows) =>
        rows.OrderBy(e => e.Position == null)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.AddedAt)
            .ThenBy(e => e.TitleKey);

    private static IOrderedQueryable<ListEntry> ApplyOrder(
        IQueryable<ListEntry> rows, ListQuery query, bool ratingAllowed)
    {
        var sort = NormalizeSort(query.Sort, ratingAllowed);
        var descending = IsDescending(sort, query.Dir);

        return sort switch
        {
            "title" => Tiebreak(By(rows, e => e.Title.Name, descending)),
            "year" => Tiebreak(ByNullsLast(rows, e => e.Title.ReleaseDate, descending)),
            "runtime" => Tiebreak(ByNullsLast(rows, e => e.Title.RuntimeMinutes, descending)),
            "score" => Tiebreak(ByNullsLast(rows, e => e.Title.TmdbVoteAverage, descending)),
            "rating" => Tiebreak(ByNullsLast(rows, e => e.Rating, descending)),
            _ => Tiebreak(By(rows, e => e.AddedAt, descending)),
        };
    }

    private static string NormalizeSort(string? sort, bool ratingAllowed) =>
        (sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "title" => "title",
            "year" => "year",
            "runtime" => "runtime",
            "score" => "score",
            // `rating` means nothing off the watched list; fall back rather than 400.
            "rating" when ratingAllowed => "rating",
            _ => "added",
        };

    private static bool IsDescending(string sort, string? dir) =>
        (dir ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "asc" => false,
            "desc" => true,
            _ => sort is "added" or "score" or "rating",
        };

    private static IOrderedQueryable<ListEntry> By<TKey>(
        IQueryable<ListEntry> rows, Expression<Func<ListEntry, TKey>> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    /// <summary>
    /// Nulls sort last in <b>both</b> directions — "no runtime recorded" belongs at
    /// the bottom whether the user asked for shortest or longest first. This matters
    /// far more since plan 08 than it did before: a series with an empty
    /// <c>episode_run_time</c> has no runtime at all, and that is ordinary rather
    /// than exceptional.
    /// </summary>
    private static IOrderedQueryable<ListEntry> ByNullsLast<TKey>(
        IQueryable<ListEntry> rows, Expression<Func<ListEntry, TKey?>> key, bool descending)
        where TKey : struct
    {
        var isNull = Expression.Lambda<Func<ListEntry, bool>>(
            Expression.Equal(key.Body, Expression.Constant(null, typeof(TKey?))),
            key.Parameters);

        var ordered = rows.OrderBy(isNull);
        return descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
    }

    /// <summary>A stable last key, so equal sort values do not shuffle between pages.</summary>
    private static IOrderedQueryable<ListEntry> Tiebreak(IOrderedQueryable<ListEntry> rows) =>
        rows.ThenBy(e => e.TitleKey);

    private static IReadOnlyList<string> InStoredOrder(IEnumerable<ListEntry> entries) =>
        entries
            .OrderBy(e => e.Position is null)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.AddedAt)
            .ThenBy(e => e.TitleKey, StringComparer.Ordinal)
            .Select(e => e.TitleKey)
            .ToArray();
}
