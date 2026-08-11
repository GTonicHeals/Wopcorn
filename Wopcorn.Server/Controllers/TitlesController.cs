using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Social;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The Catalog section of API-CONTRACT.md — films, series and seasons at one
/// grain. Every TMDB call happens behind <see cref="ITmdbClient"/>; nothing in a
/// response body names an upstream host or carries a credential (FR-B5).
/// </summary>
[Route("api/titles")]
public class TitlesController(
    ITmdbClient tmdb,
    TitleCacheService titles,
    TitleMapper mapper,
    FriendshipService friendships,
    SuggestionService suggestions,
    IMemoryCache cache) : ApiControllerBase
{
    /// <summary>TMDB refuses pages beyond 500.</summary>
    private const int MaxPage = 500;

    /// <summary>
    /// Search and discover results are shared, non-user-specific data (FR-B4). The
    /// upstream payload is cached and decorated per user afterwards, so a repeated
    /// page costs zero upstream requests (FR-B6).
    /// </summary>
    private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// One upstream request over <c>/search/multi</c>, with people discarded.
    /// TMDB's own relevance ordering across films and series is what the client
    /// sees; merging two calls by hand would cost two requests per keystroke for a
    /// worse mix.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery(Name = "type")] string[]? type = null,
        CancellationToken ct = default)
    {
        page = ClampPage(page);
        var types = SearchableTypes(type);

        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            // Contract: a blank q is an empty result set, not a 400 — and not an
            // upstream request either.
            return Ok(new TitlePage(page, 0, 0, []));
        }

        var key = $"tmdb:search:{query.ToLowerInvariant()}:{page}";
        var (payload, fromCache) = await CachedAsync(
            key, ct => tmdb.SearchMultiAsync(query, page, ct), ct);

        var summaries = (payload?.Results ?? [])
            .Where(r => (r.IsMovie && types.Contains(MediaType.Movie))
                        || (r.IsSeries && types.Contains(MediaType.Series)))
            .Select(r => r.IsMovie ? TitleSummary.From(r.AsMovie()) : TitleSummary.From(r.AsSeries()))
            .ToList();

        return Ok(await RenderAsync(
            page, summaries, payload?.Page, payload?.TotalPages, payload?.TotalResults, fromCache, ct));
    }

    /// <summary>
    /// A feed per media type — TMDB has no single "now playing" spanning both. With
    /// both types asked for, the two pages are <b>interleaved</b> rather than
    /// concatenated, so page 1 is not all films with the series below the fold.
    /// </summary>
    [HttpGet("discover/{feed}")]
    public async Task<IActionResult> Discover(
        string feed,
        [FromQuery] int page = 1,
        [FromQuery(Name = "type")] string[]? type = null,
        CancellationToken ct = default)
    {
        if (!DiscoverFeeds.TryParse(feed, out var parsed))
        {
            return Problem(404, "not_found", "That is not a feed we know about.");
        }

        page = ClampPage(page);
        var types = SearchableTypes(type);

        List<TitleSummary> movies = [];
        List<TitleSummary> series = [];
        var fromCache = true;
        int? upstreamPage = null;
        var totalPages = 0;
        var totalResults = 0;

        if (types.Contains(MediaType.Movie))
        {
            var (payload, cached) = await CachedAsync(
                $"tmdb:discover:movie:{parsed.ToWire()}:{page}",
                inner => tmdb.DiscoverAsync(parsed, page, inner), ct);

            fromCache &= cached;
            movies = (payload?.Results ?? []).Select(TitleSummary.From).ToList();
            upstreamPage ??= payload?.Page;
            totalPages = Math.Max(totalPages, payload?.TotalPages ?? 0);
            totalResults += payload?.TotalResults ?? 0;
        }

        if (types.Contains(MediaType.Series))
        {
            var (payload, cached) = await CachedAsync(
                $"tmdb:discover:tv:{parsed.ToWire()}:{page}",
                inner => tmdb.DiscoverSeriesAsync(parsed, page, inner), ct);

            fromCache &= cached;
            series = (payload?.Results ?? []).Select(TitleSummary.From).ToList();
            upstreamPage ??= payload?.Page;
            totalPages = Math.Max(totalPages, payload?.TotalPages ?? 0);
            totalResults += payload?.TotalResults ?? 0;
        }

        return Ok(await RenderAsync(
            page, Interleave(movies, series), upstreamPage, totalPages, totalResults, fromCache, ct));
    }

    [HttpGet("{key}")]
    public Task<IActionResult> Detail(string key, CancellationToken ct) =>
        DetailAsync(key, forceRefresh: false, ct);

    /// <summary>FR-B7: the manual "this looks wrong" refresh.</summary>
    [HttpPost("{key}/refresh")]
    public Task<IActionResult> Refresh(string key, CancellationToken ct) =>
        DetailAsync(key, forceRefresh: true, ct);

    private async Task<IActionResult> DetailAsync(string key, bool forceRefresh, CancellationToken ct)
    {
        if (!TryParseKey(key, out var parsed, out var failure))
        {
            return failure!;
        }

        var result = await titles.GetDetailAsync(parsed, forceRefresh, ct);

        if (result.Title is null)
        {
            return result.NotFound
                ? Problem(404, "not_found", "We could not find that title.")
                : Problem(503, "tmdb_unavailable", TmdbUnavailableFilter.Message);
        }

        var context = await mapper.LoadUserContextAsync(CurrentUserId, [parsed.Value], ct);

        // FR-G4. Friendship is re-derived here, not cached, so an unfriend removes
        // the row from this view on the very next request (NFR-4).
        var friendsWatched = await friendships.GetFriendsWatchedAsync(CurrentUserId, parsed, ct);

        // Series only: the season rows the detail fetch left behind, each already
        // decorated so its toggles and stars work from the series screen. Nothing
        // cascades — they are separate entries, and `seasonProgress` on the card is
        // the honest summary rather than a rule the data cannot support.
        var seasons = parsed.MediaType == MediaType.Series
            ? await mapper.LoadSeasonsAsync(CurrentUserId, parsed.Value, ct)
            : [];

        // Plan 10: every friend who suggested this to the viewer, accepted ones
        // included. The badge on the card is a call to action and clears when it is
        // answered; this is the record, and it stays.
        var suggestedBy = await suggestions.GetNotesAsync(CurrentUserId, parsed, ct);

        return Ok(mapper.ToDetail(
            result.Title, context, result.Stale, friendsWatched, seasons, suggestedBy));
    }

    /// <summary>
    /// The media types a search or discovery request covers. Defaults to films and
    /// series; <c>type=season</c> is ignored rather than rejected, because TMDB has
    /// no season search and a stale bookmark should still render.
    /// </summary>
    private static HashSet<MediaType> SearchableTypes(string[]? values)
    {
        var requested = (values ?? [])
            .Select(v => MediaTypes.TryParse(v, out var parsed) ? parsed : (MediaType?)null)
            .Where(t => t is MediaType.Movie or MediaType.Series)
            .Select(t => t!.Value)
            .ToHashSet();

        return requested.Count > 0 ? requested : [MediaType.Movie, MediaType.Series];
    }

    /// <summary>
    /// Alternates the two lists, then appends whatever is left of the longer one.
    /// Concatenating would put every series below every film, which on a 20-row page
    /// means the TV half is invisible.
    /// </summary>
    private static List<TitleSummary> Interleave(
        IReadOnlyList<TitleSummary> first, IReadOnlyList<TitleSummary> second)
    {
        if (first.Count == 0)
        {
            return [.. second];
        }

        if (second.Count == 0)
        {
            return [.. first];
        }

        var merged = new List<TitleSummary>(first.Count + second.Count);
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                merged.Add(first[i]);
            }

            if (i < second.Count)
            {
                merged.Add(second[i]);
            }
        }

        return merged;
    }

    /// <summary>
    /// Returns the upstream page and whether it came from the shared cache. Only a
    /// miss touches TMDB.
    /// </summary>
    private async Task<(T? Payload, bool FromCache)> CachedAsync<T>(
        string key, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return (cached, true);
        }

        var payload = await fetch(ct);
        if (payload is not null)
        {
            cache.Set(key, payload, FeedCacheTtl);
        }

        return (payload, false);
    }

    /// <summary>
    /// Upstream summaries → <see cref="TitlePage"/>. Title rows come from the
    /// database; the per-user decoration is one further query for the whole page
    /// (NFR-2).
    /// </summary>
    private async Task<TitlePage> RenderAsync(
        int page,
        IReadOnlyList<TitleSummary> summaries,
        int? upstreamPage,
        int? totalPages,
        int? totalResults,
        bool fromCache,
        CancellationToken ct)
    {
        if (summaries.Count == 0)
        {
            return new TitlePage(upstreamPage ?? page, 0, totalResults ?? 0, []);
        }

        var keys = summaries.Select(s => s.Key.Value).ToList();

        IReadOnlyDictionary<string, Title> rows;
        if (fromCache)
        {
            rows = await titles.GetManyAsync(keys, ct);

            // Only titles the cache has never seen written cost a write.
            var missing = summaries.Where(s => !rows.ContainsKey(s.Key.Value)).ToList();
            if (missing.Count > 0)
            {
                await titles.UpsertSummariesAsync(missing, ct);
                rows = await titles.GetManyAsync(keys, ct);
            }
        }
        else
        {
            rows = (await titles.UpsertSummariesAsync(summaries, ct))
                .ToDictionary(t => t.Key, StringComparer.Ordinal);
        }

        var context = await mapper.LoadUserContextAsync(CurrentUserId, keys, ct);

        var results = summaries
            .Where(s => rows.ContainsKey(s.Key.Value))
            .Select(s => mapper.ToCard(rows[s.Key.Value], context))
            .ToArray();

        return new TitlePage(
            upstreamPage ?? page,
            Math.Min(totalPages ?? 0, MaxPage),
            totalResults ?? 0,
            results);
    }

    private static int ClampPage(int page) => Math.Clamp(page, 1, MaxPage);
}
