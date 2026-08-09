using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Stands in for TMDB. Counts calls per method so a test can prove the second
/// identical search cost nothing upstream (FR-B6), and flips to
/// <see cref="Throw"/> to simulate an outage (FR-B8).
///
/// Calls are counted before the outage check, so an unexpected upstream attempt
/// shows up in the counter even when it fails.
/// </summary>
public sealed class FakeTmdbClient : ITmdbClient
{
    public const int DuneId = 438631;
    public const int DunePartTwoId = 693134;
    public const int NeverCachedId = 550;
    public const int UnknownToTmdbId = 999_999;

    /// <summary>
    /// The collision, and the reason this whole plan exists: 1396 is <i>Mirror</i>
    /// (1975) as a film and <i>Breaking Bad</i> as a series. Both are registered on
    /// the fake so a test can hold them at once.
    /// </summary>
    public const int CollisionId = 1396;

    public const int GameOfThronesId = 1399;

    public int SearchCalls { get; private set; }

    public int DiscoverCalls { get; private set; }

    public int DiscoverSeriesCalls { get; private set; }

    public int MovieCalls { get; private set; }

    public int SeriesCalls { get; private set; }

    public int SeasonCalls { get; private set; }

    public int GenreCalls { get; private set; }

    public int TvGenreCalls { get; private set; }

    public int TotalCalls =>
        SearchCalls + DiscoverCalls + DiscoverSeriesCalls + MovieCalls
        + SeriesCalls + SeasonCalls + GenreCalls + TvGenreCalls;

    /// <summary>When true every method raises <see cref="TmdbUnavailableException"/>.</summary>
    public bool Throw { get; set; }

    public List<TmdbGenre> Genres { get; } =
    [
        new(878, "Science Fiction"),
        new(12, "Adventure"),
        new(18, "Drama"),
    ];

    /// <summary>
    /// TMDB's TV list. It overlaps the film list by id where the names match (Drama
    /// 18) and adds its own (Action &amp; Adventure 10759), which is exactly the
    /// shape the union has to cope with.
    /// </summary>
    public List<TmdbGenre> TvGenres { get; } =
    [
        new(18, "Drama"),
        new(10759, "Action & Adventure"),
        new(10765, "Sci-Fi & Fantasy"),
    ];

    /// <summary>Keyed by the lower-cased query string.</summary>
    public Dictionary<string, TmdbPage<TmdbMultiSummary>> Searches { get; } = new()
    {
        ["dune"] = new TmdbPage<TmdbMultiSummary>(1, 1, 2,
        [
            MovieResult(DuneId, "Dune", "2021-09-15", 7.8),
            MovieResult(DunePartTwoId, "Dune: Part Two", "2024-02-27", 8.1),
        ]),
    };

    public Dictionary<int, TmdbMovieDetail> Movies { get; } = new()
    {
        [DuneId] = Detail(DuneId, "Dune", "2021-09-15", 7.8, 155),
    };

    public Dictionary<int, TmdbSeriesDetail> Series { get; } = [];

    /// <summary>Keyed by <c>(seriesId, seasonNumber)</c>.</summary>
    public Dictionary<(int SeriesId, int SeasonNumber), TmdbSeasonDetail> Seasons { get; } = [];

    public TmdbPage<TmdbMovieSummary> DiscoverPage { get; set; } =
        new(1, 1, 1, [MovieSummary(DuneId, "Dune", "2021-09-15", 7.8)]);

    public TmdbPage<TmdbSeriesSummary> DiscoverSeriesPage { get; set; } =
        new(1, 1, 0, []);

    /// <summary>
    /// Registers a film this fake will serve from <see cref="GetMovieAsync"/>, so a
    /// list test can build the exact catalogue its assertions need — including films
    /// with no release date, runtime or score, which is what the nulls-last ordering
    /// rule exists for.
    /// </summary>
    public FakeTmdbClient WithMovie(
        int id,
        string title,
        string? releaseDate = null,
        double? vote = null,
        int? runtime = null,
        params int[] genreIds)
    {
        Movies[id] = new TmdbMovieDetail(
            id, title, title, $"{title} overview.",
            $"/{id}-poster.jpg", $"/{id}-backdrop.jpg",
            releaseDate, vote, runtime,
            [.. genreIds.Select(g => new TmdbGenre(g, GenreName(g)))],
            new TmdbCredits([], []));

        return this;
    }

    /// <summary>
    /// Registers a series and, from <paramref name="seasonEpisodes"/>, one entry per
    /// season on its <c>seasons[]</c> array — which is what a series fetch leaves
    /// behind as season rows.
    /// </summary>
    /// <param name="episodeRunTime">
    /// Pass <c>null</c> for the Breaking Bad case: TMDB returns <c>[]</c> and the
    /// series has no derivable runtime at all. That is ordinary, not an error.
    /// </param>
    public FakeTmdbClient WithSeries(
        int id,
        string name,
        string? firstAirDate = null,
        double? vote = null,
        int? episodeRunTime = null,
        int[]? seasonEpisodes = null,
        string[]? creators = null,
        params int[] genreIds)
    {
        var episodes = seasonEpisodes ?? [];

        Series[id] = new TmdbSeriesDetail(
            id, name, name, $"{name} overview.",
            $"/{id}-poster.jpg", $"/{id}-backdrop.jpg",
            firstAirDate, vote,
            episodeRunTime is { } minutes ? [minutes] : [],
            episodes.Length == 0 ? null : episodes.Sum(),
            episodes.Length == 0 ? null : episodes.Length,
            [.. genreIds.Select(g => new TmdbGenre(g, GenreName(g)))],
            [.. (creators ?? []).Select((c, i) => new TmdbCreator(i + 1, c))],
            [
                .. episodes.Select((count, index) => new TmdbSeasonSummary(
                    100_000 + id + index,
                    index + 1,
                    $"Season {index + 1}",
                    $"{name} season {index + 1}.",
                    $"/{id}-s{index + 1}-poster.jpg",
                    firstAirDate,
                    count)),
            ],
            new TmdbCredits([], []));

        // Season details, so opening one does not have to be registered separately.
        for (var index = 0; index < episodes.Length; index++)
        {
            var number = index + 1;
            Seasons[(id, number)] = new TmdbSeasonDetail(
                100_000 + id + index,
                number,
                $"Season {number}",
                $"{name} season {number}.",
                $"/{id}-s{number}-poster.jpg",
                firstAirDate,
                vote,
                [
                    .. Enumerable.Range(1, episodes[index]).Select(e =>
                        new TmdbEpisode(e, $"Episode {e}", firstAirDate, episodeRunTime)),
                ],
                new TmdbCredits([], []));
        }

        return this;
    }

    /// <summary>Makes a search query answer with these results, in this order.</summary>
    public FakeTmdbClient WithSearch(string query, params TmdbMultiSummary[] results) =>
        WithSearch(query, results.Length, results);

    public FakeTmdbClient WithSearch(
        string query, int totalResults, params TmdbMultiSummary[] results)
    {
        Searches[query.ToLowerInvariant()] =
            new TmdbPage<TmdbMultiSummary>(1, 1, totalResults, results);
        return this;
    }

    /// <summary>A <c>/search/multi</c> row for a film.</summary>
    public static TmdbMultiSummary MovieResult(
        int id, string title, string? releaseDate = null, double? vote = null) =>
        new(id, "movie", title, title, null, null, $"{title} overview.",
            $"/{id}-poster.jpg", $"/{id}-backdrop.jpg", releaseDate, null, vote, [878, 12]);

    /// <summary>A <c>/search/multi</c> row for a series — different field names.</summary>
    public static TmdbMultiSummary SeriesResult(
        int id, string name, string? firstAirDate = null, double? vote = null) =>
        new(id, "tv", null, null, name, name, $"{name} overview.",
            $"/{id}-poster.jpg", $"/{id}-backdrop.jpg", null, firstAirDate, vote, [18]);

    /// <summary>
    /// A person row. <c>/search/multi</c> returns these mixed in, and they must be
    /// discarded before anything else sees the page (D-5).
    /// </summary>
    public static TmdbMultiSummary PersonResult(int id, string name) =>
        new(id, "person", null, null, name, name, null, null, null, null, null, null, null);

    private string GenreName(int id) =>
        Genres.FirstOrDefault(g => g.Id == id)?.Name
        ?? TvGenres.FirstOrDefault(g => g.Id == id)?.Name
        ?? $"Genre {id}";

    public Task<TmdbPage<TmdbMultiSummary>?> SearchMultiAsync(
        string query, int page, CancellationToken ct)
    {
        SearchCalls++;
        Guard();

        Searches.TryGetValue(query.ToLowerInvariant(), out var result);
        return Task.FromResult<TmdbPage<TmdbMultiSummary>?>(
            result ?? new TmdbPage<TmdbMultiSummary>(page, 0, 0, []));
    }

    public Task<TmdbPage<TmdbMovieSummary>?> DiscoverAsync(
        DiscoverFeed feed, int page, CancellationToken ct)
    {
        DiscoverCalls++;
        Guard();

        return Task.FromResult<TmdbPage<TmdbMovieSummary>?>(DiscoverPage);
    }

    public Task<TmdbPage<TmdbSeriesSummary>?> DiscoverSeriesAsync(
        DiscoverFeed feed, int page, CancellationToken ct)
    {
        DiscoverSeriesCalls++;
        Guard();

        return Task.FromResult<TmdbPage<TmdbSeriesSummary>?>(DiscoverSeriesPage);
    }

    public Task<TmdbMovieDetail?> GetMovieAsync(int id, CancellationToken ct)
    {
        MovieCalls++;
        Guard();

        return Task.FromResult(Movies.GetValueOrDefault(id));
    }

    public Task<TmdbSeriesDetail?> GetSeriesAsync(int id, CancellationToken ct)
    {
        SeriesCalls++;
        Guard();

        return Task.FromResult(Series.GetValueOrDefault(id));
    }

    public Task<TmdbSeasonDetail?> GetSeasonAsync(
        int seriesId, int seasonNumber, CancellationToken ct)
    {
        SeasonCalls++;
        Guard();

        return Task.FromResult(Seasons.GetValueOrDefault((seriesId, seasonNumber)));
    }

    public Task<IReadOnlyList<TmdbGenre>> GetGenresAsync(CancellationToken ct)
    {
        GenreCalls++;
        Guard();

        return Task.FromResult<IReadOnlyList<TmdbGenre>>(Genres);
    }

    public Task<IReadOnlyList<TmdbGenre>> GetTvGenresAsync(CancellationToken ct)
    {
        TvGenreCalls++;
        Guard();

        return Task.FromResult<IReadOnlyList<TmdbGenre>>(TvGenres);
    }

    private void Guard()
    {
        if (Throw)
        {
            throw new TmdbUnavailableException("Simulated TMDB outage.");
        }
    }

    private static TmdbMovieSummary MovieSummary(
        int id, string title, string releaseDate, double vote) =>
        new(id, title, title, $"{title} overview.", $"/{id}-poster.jpg", $"/{id}-backdrop.jpg",
            releaseDate, vote, [878, 12]);

    private static TmdbMovieDetail Detail(
        int id, string title, string releaseDate, double vote, int runtime) =>
        new(id, title, title, $"{title} overview.", $"/{id}-poster.jpg", $"/{id}-backdrop.jpg",
            releaseDate, vote, runtime,
            [new TmdbGenre(878, "Science Fiction"), new TmdbGenre(12, "Adventure")],
            new TmdbCredits(
                [
                    new TmdbCastMember("Timothée Chalamet", "Paul Atreides", "/paul.jpg", 0),
                    new TmdbCastMember("Rebecca Ferguson", "Lady Jessica", "/jessica.jpg", 1),
                ],
                [
                    new TmdbCrewMember("Denis Villeneuve", "Director"),
                    new TmdbCrewMember("Greig Fraser", "Director of Photography"),
                ]));
}
