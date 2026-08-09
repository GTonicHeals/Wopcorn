using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Catalog;

/// <summary>
/// Owns the <c>Genres</c> table, which mirrors the <b>union</b> of TMDB's film and
/// TV genre lists. Both lists are memory-cached for 24 hours inside
/// <see cref="TmdbClient"/> and mirrored here, so genre filters keep working with
/// TMDB unreachable (FR-B8). Nothing on this type throws
/// <see cref="TmdbUnavailableException"/> — a failed refresh degrades to the
/// mirrored copy.
/// </summary>
/// <remarks>
/// The two upstream lists overlap by id where the names match (Drama 18, Comedy
/// 35) and TV contributes its own (Action &amp; Adventure 10759, Kids 10762,
/// 10763–10768), so the union is conflict-free. Which side a genre came from is
/// kept per row, because the client's filter sheet has to say so.
/// </remarks>
public sealed class GenreCatalogService(
    WopcornDbContext db,
    ITmdbClient tmdb,
    ILogger<GenreCatalogService> logger)
{
    /// <summary>The list behind <c>GET /api/genres</c>. Database-first by design.</summary>
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);

        var rows = await db.Genres
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return rows
            .Select(g => new GenreDto(
                g.TmdbId,
                g.Name,
                g.MediaTypes().Select(t => t.ToWire()).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// The genre ids a <c>TitleGenre</c> row may legally reference. Search results
    /// carry bare <c>genre_ids</c>, so the catalog has to exist before the join rows
    /// do or the foreign key fails.
    /// </summary>
    public async Task<HashSet<int>> KnownIdsAsync(CancellationToken ct)
    {
        // A successful refresh already loaded the whole catalog; only fall back to a
        // second query when TMDB could not answer.
        if (await RefreshAsync(ct) is { } refreshed)
        {
            return [.. refreshed.Keys];
        }

        var ids = await db.Genres.AsNoTracking().Select(g => g.TmdbId).ToListAsync(ct);
        return [.. ids];
    }

    /// <summary>
    /// Adds or renames rows for genres that arrived with names (a detail payload),
    /// recording which side of the catalog they came from. Deliberately does not
    /// save — the caller's single <c>SaveChanges</c> covers it.
    /// </summary>
    public async Task<Dictionary<int, Genre>> EnsureAsync(
        IReadOnlyCollection<TmdbGenre> upstream, MediaType mediaType, CancellationToken ct)
    {
        var named = upstream.Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList();
        if (named.Count == 0)
        {
            return [];
        }

        var ids = named.Select(g => g.Id).ToList();
        var rows = await db.Genres.Where(g => ids.Contains(g.TmdbId)).ToDictionaryAsync(g => g.TmdbId, ct);

        // The query above cannot see rows this unit of work has already added but
        // not yet saved, and the union guarantees that case: Drama is 18 on both
        // TMDB lists, so the TV pass would otherwise add a second entity with the
        // same key and EF would refuse to track it. Only ids this payload actually
        // mentions are merged in — pulling in every tracked genre would attach
        // unrelated ones to whatever title the caller is about to sync.
        foreach (var tracked in db.Genres.Local.Where(g => ids.Contains(g.TmdbId)))
        {
            rows.TryAdd(tracked.TmdbId, tracked);
        }

        foreach (var genre in named)
        {
            if (!rows.TryGetValue(genre.Id, out var row))
            {
                row = new Genre { TmdbId = genre.Id, Name = genre.Name! };
                db.Genres.Add(row);
                rows[genre.Id] = row;
            }
            else if (row.Name != genre.Name)
            {
                row.Name = genre.Name!;
            }

            Mark(row, mediaType);
        }

        return rows;
    }

    /// <summary>
    /// Records that TMDB lists this genre for one side of its catalog. Additive: a
    /// genre both lists (Drama) ends up flagged for both, and a refresh of one list
    /// never clears the other's flag.
    /// </summary>
    private static void Mark(Genre row, MediaType mediaType)
    {
        if (mediaType == MediaType.Movie)
        {
            row.InMovies = true;
        }
        else
        {
            // Series and seasons alike: a season's genres are its series' genres.
            row.InTv = true;
        }
    }

    /// <summary>
    /// Best-effort mirror of both upstream catalogs. Returns the catalog rows on
    /// success and <c>null</c> when neither list could be consulted — a partial
    /// answer still counts, because half a genre list beats none.
    /// </summary>
    private async Task<Dictionary<int, Genre>?> RefreshAsync(CancellationToken ct)
    {
        var movies = await SafeAsync(tmdb.GetGenresAsync, "film", ct);
        var tv = await SafeAsync(tmdb.GetTvGenresAsync, "TV", ct);

        if (movies.Count == 0 && tv.Count == 0)
        {
            return null;
        }

        var rows = await EnsureAsync(movies, MediaType.Movie, ct);
        foreach (var (id, row) in await EnsureAsync(tv, MediaType.Series, ct))
        {
            rows[id] = row;
        }

        await db.SaveChangesAsync(ct);
        return rows;
    }

    private async Task<IReadOnlyList<TmdbGenre>> SafeAsync(
        Func<CancellationToken, Task<IReadOnlyList<TmdbGenre>>> fetch,
        string which,
        CancellationToken ct)
    {
        try
        {
            return await fetch(ct);
        }
        catch (TmdbUnavailableException)
        {
            logger.LogWarning("TMDB {Which} genre list unavailable; serving the mirrored table.", which);
            return [];
        }
    }
}
