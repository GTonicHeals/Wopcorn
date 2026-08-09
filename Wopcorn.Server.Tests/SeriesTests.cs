using System.Net;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Plan 08 — films, series and seasons at one grain.
///
/// The headline is the <b>id collision</b>: TMDB's film and TV ids are separate
/// namespaces that overlap, and 1396 is a real example of both (<i>Mirror</i> and
/// <i>Breaking Bad</i>). Everything else here follows from keeping them apart.
/// </summary>
public class SeriesTests
{
    private const int Collision = FakeTmdbClient.CollisionId;   // 1396

    private static string MovieKey => TestApi.Movie(Collision);       // movie-1396
    private static string SeriesKey => TestApi.Series(Collision);     // tv-1396

    private static async Task<(WopcornApiFactory Factory, HttpClient Client, FakeTmdbClient Tmdb)>
        WorldAsync(Action<FakeTmdbClient>? configure = null)
    {
        var tmdb = new FakeTmdbClient();
        tmdb.WithMovie(Collision, "Mirror", "1975-03-07", 8.1, 107, 18)
            .WithSeries(Collision, "Breaking Bad", "2008-01-20", 8.9,
                // TMDB returns [] for Breaking Bad's episode_run_time — the case the
                // whole null-runtime rule exists for.
                episodeRunTime: null,
                seasonEpisodes: [7, 13, 13, 13, 16],
                creators: ["Vince Gilligan"],
                genreIds: 18);

        configure?.Invoke(tmdb);

        var factory = new WopcornApiFactory { TmdbClient = tmdb };
        var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("series@example.com", "password1", "Series User");

        return (factory, client, tmdb);
    }

    // --- the collision ------------------------------------------------------

    [Fact]
    public async Task Movie_1396_and_tv_1396_are_different_titles()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        var film = await client.GetTitleAsync(MovieKey);
        var series = await client.GetTitleAsync(SeriesKey);

        Assert.Equal("Mirror", film.Title);
        Assert.Equal("movie", film.MediaType);

        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal("series", series.MediaType);

        // Same TMDB id, different keys — which is the entire point.
        Assert.Equal(Collision, film.TmdbId);
        Assert.Equal(Collision, series.TmdbId);
        Assert.NotEqual(film.Key, series.Key);

        // And both rows coexist in the catalog.
        var rows = await factory.TitlesAsync();
        Assert.Contains(rows, t => t.Key == MovieKey && t.MediaType == MediaType.Movie);
        Assert.Contains(rows, t => t.Key == SeriesKey && t.MediaType == MediaType.Series);
    }

    [Fact]
    public async Task A_film_and_a_series_sharing_an_id_have_separate_entries_and_ratings()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        await client.AddAndReadAsync("watched", MovieKey);
        Assert.Equal(HttpStatusCode.OK, (await client.RateAsync(MovieKey, 9)).StatusCode);

        await client.AddAndReadAsync("watchlist", SeriesKey);

        var watched = await client.GetListAsync("watched");
        var entry = Assert.Single(watched.Entries);
        Assert.Equal(MovieKey, entry.Title.Key);
        Assert.Equal(9, entry.Rating);

        var watchlist = await client.GetListAsync("watchlist");
        Assert.Equal(SeriesKey, Assert.Single(watchlist.Entries).Title.Key);

        // Rating the film left the series untouched, in both directions.
        var series = await client.GetTitleAsync(SeriesKey);
        Assert.Null(series.MyRating);
        var film = await client.GetTitleAsync(MovieKey);
        Assert.Equal(9, film.MyRating);
    }

    [Fact]
    public async Task Taste_match_never_pairs_a_film_with_a_series_of_the_same_id()
    {
        using var world = await SocialWorld.CreateAsync();

        world.Tmdb.WithMovie(Collision, "Mirror", "1975-03-07", 8.1, 107, 18)
            .WithSeries(Collision, "Breaking Bad", "2008-01-20", 8.9,
                episodeRunTime: 47, seasonEpisodes: [7], genreIds: 18);

        var (me, them) = await world.JoinFriendsAsync("mira", "wes");

        // One rated Mirror; the other rated Breaking Bad. Same TMDB id, and yet
        // nothing at all in common.
        Assert.Equal(HttpStatusCode.OK, (await me.Client.RateAsync(MovieKey, 9)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await them.Client.RateAsync(SeriesKey, 9)).StatusCode);

        var friends = await me.Client.GetFriendsAsync();
        var match = Assert.Single(friends.Friends).TasteMatch;

        Assert.Equal(0, match.SharedCount);
        Assert.Null(match.Score);
        Assert.False(match.Qualified);
    }

    // --- key parsing --------------------------------------------------------

    [Theory]
    [InlineData("tv-abc")]
    [InlineData("movie-1-s2")]      // films have no seasons
    [InlineData("show-5")]
    [InlineData("movie-")]
    [InlineData("tv-01")]           // a second spelling of tv-1 is not a key
    [InlineData("movie-1.5")]
    public async Task A_key_that_does_not_parse_is_400_and_never_404(string key)
    {
        var (factory, client, tmdb) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        var response = await client.GetAsync($"/api/titles/{key}");

        // A malformed identifier is a bad request, not a missing title: the caller
        // never named something that could have been found.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);

        // And it cost nothing upstream.
        Assert.Equal(0, tmdb.MovieCalls + tmdb.SeriesCalls + tmdb.SeasonCalls);
    }

    [Fact]
    public async Task Every_key_taking_route_rejects_a_malformed_key_the_same_way()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        var responses = new[]
        {
            await client.PutAsync("/api/lists/watched/tv-abc", null),
            await client.DeleteAsync("/api/lists/watched/tv-abc"),
            await client.RateAsync("tv-abc", 5),
            await client.ClearRatingAsync("tv-abc"),
            await client.PostAsync("/api/titles/tv-abc/refresh", null),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
        }
    }

    // --- seasons ------------------------------------------------------------

    [Fact]
    public async Task Opening_a_series_costs_one_call_and_leaves_its_season_rows_behind()
    {
        var (factory, client, tmdb) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        var series = await client.GetTitleAsync(SeriesKey);

        // One upstream request, not one per season (FR-B6).
        Assert.Equal(1, tmdb.SeriesCalls);
        Assert.Equal(0, tmdb.SeasonCalls);

        Assert.Equal(5, series.SeasonCount);
        Assert.Equal(62, series.EpisodeCount);
        Assert.Equal(["Vince Gilligan"], series.Creators);
        Assert.Null(series.Director);          // series have no single director

        // Five season rows, in order, each addressable on its own.
        Assert.Equal(
            [1, 2, 3, 4, 5],
            series.Seasons.Select(s => s.SeasonNumber));
        Assert.Equal(
            [
                TestApi.Season(Collision, 1), TestApi.Season(Collision, 2),
                TestApi.Season(Collision, 3), TestApi.Season(Collision, 4),
                TestApi.Season(Collision, 5),
            ],
            series.Seasons.Select(s => s.Key));
        Assert.Equal([7, 13, 13, 13, 16], series.Seasons.Select(s => s.EpisodeCount));

        var stored = await factory.SeasonsOfAsync(SeriesKey);
        Assert.Equal(5, stored.Count);
        Assert.All(stored, row =>
        {
            Assert.Equal(MediaType.Season, row.MediaType);
            Assert.Equal(SeriesKey, row.ParentKey);
            Assert.Equal(Collision, row.TmdbId);
        });
    }

    [Fact]
    public async Task A_series_with_no_episode_run_time_has_a_null_runtime_and_does_not_throw()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        // Breaking Bad returns episode_run_time: []. Null is the correct answer.
        var series = await client.GetTitleAsync(SeriesKey);
        Assert.Null(series.RuntimeMinutes);

        var season = await client.GetTitleAsync(TestApi.Season(Collision, 2));
        Assert.Null(season.RuntimeMinutes);
        Assert.Equal(13, season.EpisodeCount);
    }

    [Fact]
    public async Task A_series_with_an_episode_run_time_derives_a_runtime_for_itself_and_its_seasons()
    {
        var (factory, client, _) = await WorldAsync(tmdb =>
            tmdb.WithSeries(FakeTmdbClient.GameOfThronesId, "Game of Thrones", "2011-04-17", 8.4,
                episodeRunTime: 60, seasonEpisodes: [10, 10], genreIds: 18));
        using var _f = factory;
        using var _c = client;

        var key = TestApi.Series(FakeTmdbClient.GameOfThronesId);

        var series = await client.GetTitleAsync(key);
        Assert.Equal(60 * 20, series.RuntimeMinutes);

        var season = await client.GetTitleAsync(TestApi.Season(FakeTmdbClient.GameOfThronesId, 1));
        Assert.Equal(60 * 10, season.RuntimeMinutes);
    }

    [Fact]
    public async Task Opening_a_season_directly_creates_its_series_row_first()
    {
        var (factory, client, tmdb) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        // Straight to a season, with nothing cached: a season may never exist
        // without its series, because ParentKey is a foreign key.
        var season = await client.GetTitleAsync(TestApi.Season(Collision, 3));

        Assert.Equal("season", season.MediaType);
        Assert.Equal(3, season.SeasonNumber);
        Assert.Equal(SeriesKey, season.ParentKey);

        var rows = await factory.TitlesAsync();
        Assert.Contains(rows, t => t.Key == SeriesKey);
        Assert.Equal(1, tmdb.SeriesCalls);
        Assert.Equal(1, tmdb.SeasonCalls);
    }

    [Fact]
    public async Task A_seasons_genres_are_its_series_genres()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        var series = await client.GetTitleAsync(SeriesKey);
        var season = await client.GetTitleAsync(TestApi.Season(Collision, 1));

        // TMDB season objects carry no genres at all, so a season inherits its
        // series' — otherwise a genre filter would silently exclude every season.
        Assert.NotEmpty(series.GenreIds);
        Assert.Equal(series.GenreIds, season.GenreIds);

        // And that inheritance is real enough to filter on.
        await client.AddAndReadAsync("watchlist", TestApi.Season(Collision, 1));

        var genre = series.GenreIds[0];
        var filtered = await client.GetListAsync("watchlist", $"?genre={genre}");
        Assert.Equal(
            TestApi.Season(Collision, 1),
            Assert.Single(filtered.Entries).Title.Key);
    }

    // --- no cascade ---------------------------------------------------------

    [Fact]
    public async Task A_series_and_its_seasons_are_independent_in_both_directions()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        await client.GetTitleAsync(SeriesKey);          // materialise the season rows

        var seasonTwo = TestApi.Season(Collision, 2);

        // Watching a season does not watch the series (D-2).
        await client.AddAndReadAsync("watched", seasonTwo);
        Assert.False((await client.GetTitleAsync(SeriesKey)).SeasonProgress is null);
        Assert.Null((await client.GetTitleAsync(SeriesKey)).MyRating);

        var series = await client.GetTitleAsync(SeriesKey);
        Assert.False(series.Seasons.Single(s => s.SeasonNumber == 1).Lists.Watched);
        Assert.True(series.Seasons.Single(s => s.SeasonNumber == 2).Lists.Watched);

        // Rating the series does not rate its seasons, either.
        Assert.Equal(HttpStatusCode.OK, (await client.RateAsync(SeriesKey, 10)).StatusCode);

        var after = await client.GetTitleAsync(SeriesKey);
        Assert.Equal(10, after.MyRating);
        Assert.All(after.Seasons, season => Assert.Null(season.MyRating));
    }

    [Fact]
    public async Task Season_progress_counts_watched_seasons_and_never_implies_the_series()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        await client.GetTitleAsync(SeriesKey);

        // Nothing watched: no progress at all, rather than 0 / 5.
        Assert.Null((await client.GetTitleAsync(SeriesKey)).SeasonProgress);

        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 1));
        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 2));
        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 3));

        var progress = (await client.GetTitleAsync(SeriesKey)).SeasonProgress;
        Assert.NotNull(progress);
        Assert.Equal(3, progress.Watched);
        Assert.Equal(5, progress.Total);

        // All five watched reads 5 / 5 — and still says nothing about the series.
        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 4));
        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 5));

        var full = await client.GetTitleAsync(SeriesKey);
        Assert.Equal(5, full.SeasonProgress!.Watched);
        Assert.Equal(5, full.SeasonProgress.Total);
        Assert.False(full.Seasons.Length == 0);

        var watched = await client.GetListAsync("watched");
        Assert.DoesNotContain(watched.Entries, e => e.Title.Key == SeriesKey);
    }

    [Fact]
    public async Task A_series_card_in_a_list_carries_its_own_season_progress()
    {
        var (factory, client, _) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        await client.GetTitleAsync(SeriesKey);
        await client.AddAndReadAsync("watchlist", SeriesKey);
        await client.AddAndReadAsync("watched", TestApi.Season(Collision, 1));

        // The grid has to be able to say "1 / 5 seasons" without opening it.
        var page = await client.GetListAsync("watchlist");
        var card = Assert.Single(page.Entries).Title;

        Assert.NotNull(card.SeasonProgress);
        Assert.Equal(1, card.SeasonProgress.Watched);
        Assert.Equal(5, card.SeasonProgress.Total);
    }

    // --- search and discover ------------------------------------------------

    [Fact]
    public async Task Search_returns_films_and_series_together_and_discards_people()
    {
        var (factory, client, tmdb) = await WorldAsync(t => t.WithSearch(
            "bad",
            FakeTmdbClient.MovieResult(Collision, "Mirror", "1975-03-07", 8.1),
            FakeTmdbClient.PersonResult(500, "Bryan Cranston"),
            FakeTmdbClient.SeriesResult(Collision, "Breaking Bad", "2008-01-20", 8.9)));
        using var _f = factory;
        using var _c = client;

        var page = await client.SearchTitlesAsync("bad");

        // One upstream request over /search/multi (D-5), with the person dropped and
        // TMDB's own ordering across the two types preserved.
        Assert.Equal(1, tmdb.SearchCalls);
        Assert.Equal([MovieKey, SeriesKey], page.Results.Select(r => r.Key));
        Assert.Equal(["movie", "series"], page.Results.Select(r => r.MediaType));
    }

    [Fact]
    public async Task Search_can_be_narrowed_to_one_media_type()
    {
        var (factory, client, _) = await WorldAsync(t => t.WithSearch(
            "bad",
            FakeTmdbClient.MovieResult(Collision, "Mirror", "1975-03-07", 8.1),
            FakeTmdbClient.SeriesResult(Collision, "Breaking Bad", "2008-01-20", 8.9)));
        using var _f = factory;
        using var _c = client;

        var seriesOnly = await (await client.GetAsync("/api/titles/search?q=bad&type=series"))
            .ReadAsAsync<TitlePageDto>();
        Assert.Equal([SeriesKey], seriesOnly.Results.Select(r => r.Key));

        // type=season is meaningless here and is ignored rather than rejected —
        // TMDB has no season search.
        var seasonAsked = await (await client.GetAsync("/api/titles/search?q=bad&type=season"))
            .ReadAsAsync<TitlePageDto>();
        Assert.Equal([MovieKey, SeriesKey], seasonAsked.Results.Select(r => r.Key));
    }

    [Fact]
    public async Task Discover_interleaves_films_and_series_rather_than_concatenating_them()
    {
        var (factory, client, tmdb) = await WorldAsync();
        using var _f = factory;
        using var _c = client;

        tmdb.DiscoverPage = new TmdbPage<TmdbMovieSummary>(1, 1, 2,
        [
            new(9001, "Film A", "Film A", null, null, null, "2001-01-01", 7.0, [18]),
            new(9002, "Film B", "Film B", null, null, null, "2002-01-01", 7.0, [18]),
        ]);
        tmdb.DiscoverSeriesPage = new TmdbPage<TmdbSeriesSummary>(1, 1, 2,
        [
            new(9101, "Series A", "Series A", null, null, null, "2001-01-01", 7.0, [18]),
            new(9102, "Series B", "Series B", null, null, null, "2002-01-01", 7.0, [18]),
        ]);

        var page = await (await client.GetAsync("/api/titles/discover/popular"))
            .ReadAsAsync<TitlePageDto>();

        // Concatenating would put every series below every film, which on one page
        // means the TV half is invisible.
        Assert.Equal(
            ["movie-9001", "tv-9101", "movie-9002", "tv-9102"],
            page.Results.Select(r => r.Key));
    }
}
