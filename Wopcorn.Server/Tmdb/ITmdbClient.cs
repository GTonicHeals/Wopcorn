using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tmdb;

/// <summary>
/// The only door to api.themoviedb.org. Everything else in the server talks to
/// this interface, which is what lets the test harness substitute a fake and
/// guarantees no test reaches the network (00-testing.md).
///
/// A missing title comes back as <c>null</c>; every other failure is a
/// <see cref="TmdbUnavailableException"/>.
/// </summary>
public interface ITmdbClient
{
    /// <summary>
    /// <c>/search/multi</c> — films and series in one relevance-ordered page, with
    /// people still in it for the caller to discard.
    /// </summary>
    /// <remarks>
    /// One upstream request per search keeps FR-B6's "one query, one call"
    /// property, and TMDB's own cross-type ordering beats any merge rule invented
    /// here. Two calls merged locally would double the request cost of every
    /// keystroke for a worse mix.
    /// </remarks>
    Task<TmdbPage<TmdbMultiSummary>?> SearchMultiAsync(string query, int page, CancellationToken ct);

    /// <summary>One discovery feed for one media type; seasons have none.</summary>
    Task<TmdbPage<TmdbMovieSummary>?> DiscoverAsync(DiscoverFeed feed, int page, CancellationToken ct);

    /// <inheritdoc cref="DiscoverAsync"/>
    Task<TmdbPage<TmdbSeriesSummary>?> DiscoverSeriesAsync(
        DiscoverFeed feed, int page, CancellationToken ct);

    Task<TmdbMovieDetail?> GetMovieAsync(int id, CancellationToken ct);

    Task<TmdbSeriesDetail?> GetSeriesAsync(int id, CancellationToken ct);

    Task<TmdbSeasonDetail?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken ct);

    /// <summary><c>/genre/movie/list</c>.</summary>
    Task<IReadOnlyList<TmdbGenre>> GetGenresAsync(CancellationToken ct);

    /// <summary><c>/genre/tv/list</c>. Seeded into the same table as the film list.</summary>
    Task<IReadOnlyList<TmdbGenre>> GetTvGenresAsync(CancellationToken ct);

    /// <summary>
    /// <c>/movie/{id}/watch/providers</c> or <c>/tv/{id}/watch/providers</c> — who
    /// carries this title, keyed by region.
    /// </summary>
    /// <param name="mediaType">
    /// <see cref="MediaType.Movie"/> or <see cref="MediaType.Series"/> only. TMDB
    /// exposes providers at those two grains and nowhere else, so passing
    /// <see cref="MediaType.Season"/> is a programming error and throws
    /// <see cref="ArgumentOutOfRangeException"/>: resolving a season to its series
    /// is the caller's job, and a silent fallback here would hide the one mapping
    /// worth being explicit about.
    /// </param>
    /// <remarks>
    /// A 404 means "TMDB has no provider data for this title", not "no such
    /// title" — it comes back as <c>null</c> and must never be conflated with the
    /// detail 404 that produces <c>not_found</c>.
    /// </remarks>
    Task<TmdbWatchProviders?> GetWatchProvidersAsync(
        MediaType mediaType, int tmdbId, CancellationToken ct);

    /// <summary>
    /// <c>/watch/providers/{movie|tv}?watch_region=XX</c> — the services TMDB knows
    /// about in one region, for one side of its catalog.
    /// </summary>
    Task<IReadOnlyList<TmdbProviderDirectoryEntry>> GetProviderDirectoryAsync(
        MediaType mediaType, string region, CancellationToken ct);
}

/// <summary>
/// How a runtime is derived per media type — the one rule the whole app depends
/// on being applied identically wherever a title is written.
/// </summary>
/// <remarks>
/// TMDB gives a film a scalar <c>runtime</c> but gives a series an
/// <c>episode_run_time</c> <b>array</b>, and that array is frequently empty:
/// Breaking Bad returns <c>[]</c>. So a null runtime is normal for a series
/// rather than exceptional, and the two places that consume runtime are built for
/// it — the <c>runtime</c> sort puts nulls last in both directions, and a list's
/// runtime total sums only the runtimes that are known.
/// </remarks>
public static class TmdbRuntime
{
    /// <summary>Episode length × episode count, or null when either is unknown.</summary>
    public static int? ForEpisodes(IReadOnlyList<int>? episodeRunTime, int? episodeCount)
    {
        var perEpisode = episodeRunTime is { Count: > 0 } ? episodeRunTime[0] : 0;
        if (perEpisode <= 0 || episodeCount is not > 0)
        {
            return null;
        }

        return perEpisode * episodeCount.Value;
    }
}
