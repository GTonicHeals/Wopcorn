using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wopcorn.Server.Data;

namespace Wopcorn.Server.Tests;

/// <summary>API-CONTRACT.md "Catalog" — be-02.</summary>
public class CatalogTests
{
    private record ListMembershipDto(bool Watched, bool Watchlist, bool Queue);

    private record FilmCardDto(
        string Key,
        string MediaType,
        int TmdbId,
        int? SeasonNumber,
        string? ParentKey,
        string Title,
        int? ReleaseYear,
        string? PosterPath,
        double? TmdbVoteAverage,
        int? RuntimeMinutes,
        int? EpisodeCount,
        int? SeasonCount,
        int[] GenreIds,
        ListMembershipDto Lists,
        int? MyRating);

    private record GenreDto(int Id, string Name, string[] MediaTypes);

    private record CastMemberDto(string Name, string? Character, string? ProfilePath);

    private record SeasonRowDto(
        string Key, int SeasonNumber, string Name, int? EpisodeCount, string? AirDate);

    private record TitleDetailDto(
        string Key,
        string MediaType,
        int TmdbId,
        int? SeasonNumber,
        string? ParentKey,
        string Title,
        int? ReleaseYear,
        int? RuntimeMinutes,
        int? EpisodeCount,
        int? SeasonCount,
        int[] GenreIds,
        ListMembershipDto Lists,
        int? MyRating,
        string? BackdropPath,
        string? Overview,
        string? ReleaseDate,
        GenreDto[] Genres,
        string? Director,
        string[] Creators,
        CastMemberDto[] Cast,
        SeasonRowDto[] Seasons,
        object[] FriendsWatched,
        bool Stale);

    private record FilmPageDto(int Page, int TotalPages, int TotalResults, FilmCardDto[] Results);

    /// <summary>A factory with the fake wired in, plus a signed-in client.</summary>
    private static async Task<(WopcornApiFactory Factory, HttpClient Client)> SignedInAsync(
        FakeTmdbClient fake)
    {
        var factory = new WopcornApiFactory { TmdbClient = fake };
        var client = factory.CreateSessionClient();
        await client.RegisterAndReadAsync("catalog@example.com", "password1", "Catalog User");
        return (factory, client);
    }

    [Fact]
    public async Task Search_calls_TMDB_once_then_answers_from_the_database()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        var first = await client.GetAsync("/api/titles/search?q=dune");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var page = await first.ReadAsAsync<FilmPageDto>();
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.TotalResults);
        Assert.Equal([FakeTmdbClient.DuneId, FakeTmdbClient.DunePartTwoId],
            page.Results.Select(r => r.TmdbId));
        Assert.Equal("Dune", page.Results[0].Title);
        Assert.Equal(2021, page.Results[0].ReleaseYear);
        Assert.Equal([12, 878], page.Results[0].GenreIds);
        Assert.False(page.Results[0].Lists.Watched);
        Assert.Null(page.Results[0].MyRating);
        Assert.Equal(1, fake.SearchCalls);

        // FR-B6: the films are now local.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WopcornDbContext>();
            Assert.Equal(2, await db.Titles.CountAsync());
            Assert.Equal(4, await db.Set<Wopcorn.Server.Data.Entities.TitleGenre>().CountAsync());
        }

        // With upstream now failing, the repeat still answers in full — which is
        // only possible from the local copy.
        fake.Throw = true;

        var second = await client.GetAsync("/api/titles/search?q=dune");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var repeat = await second.ReadAsAsync<FilmPageDto>();
        Assert.Equal(page.Results.Select(r => r.TmdbId), repeat.Results.Select(r => r.TmdbId));
        Assert.Equal("Dune: Part Two", repeat.Results[1].Title);
        Assert.Equal(1, fake.SearchCalls);
    }

    [Fact]
    public async Task Blank_query_is_an_empty_page_and_never_reaches_TMDB()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        foreach (var query in new[] { string.Empty, "%20%20" })
        {
            var response = await client.GetAsync($"/api/titles/search?q={query}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.ReadAsAsync<FilmPageDto>();
            Assert.Empty(page.Results);
            Assert.Equal(0, page.TotalResults);
            Assert.Equal(0, page.TotalPages);
        }

        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Detail_returns_the_full_contract_shape()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        var response = await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var film = await response.ReadAsAsync<TitleDetailDto>();

        Assert.Equal(FakeTmdbClient.DuneId, film.TmdbId);
        Assert.Equal("Dune", film.Title);
        Assert.Equal(155, film.RuntimeMinutes);
        Assert.Equal("2021-09-15", film.ReleaseDate);
        Assert.Equal(2021, film.ReleaseYear);
        Assert.Equal("Denis Villeneuve", film.Director);
        Assert.Equal(["Timothée Chalamet", "Rebecca Ferguson"], film.Cast.Select(c => c.Name));
        Assert.Equal("Paul Atreides", film.Cast[0].Character);
        Assert.Equal([12, 878], film.Genres.Select(g => g.Id));
        Assert.Equal("Adventure", film.Genres[0].Name);
        Assert.Empty(film.FriendsWatched);   // be-04 fills this
        Assert.False(film.Stale);

        // FR-B5: nothing upstream-shaped leaks into the body.
        var body = await (await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("themoviedb", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_within_the_TTL_does_not_call_TMDB_again()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}");
        Assert.Equal(1, fake.MovieCalls);

        await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}");
        Assert.Equal(1, fake.MovieCalls);

        // FR-B7: refresh is the escape hatch that ignores the TTL.
        var refreshed = await client.PostAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(2, fake.MovieCalls);
    }

    [Fact]
    public async Task With_TMDB_down_a_cached_film_is_stale_an_unknown_one_is_503_and_genres_still_answer()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        // Cache Dune as a search summary: it has a row but no detail fetch, so the
        // next detail read must try upstream.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/titles/search?q=dune")).StatusCode);

        fake.Throw = true;

        var cached = await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.DuneId)}");
        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);          // FR-B8
        var film = await cached.ReadAsAsync<TitleDetailDto>();
        Assert.True(film.Stale);
        Assert.Equal("Dune", film.Title);

        var uncached = await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.NeverCachedId)}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, uncached.StatusCode);
        var error = await uncached.ReadApiErrorAsync();
        Assert.Equal("tmdb_unavailable", error.Code);

        var genres = await client.GetAsync("/api/genres");
        Assert.Equal(HttpStatusCode.OK, genres.StatusCode);
        var list = await genres.ReadAsAsync<GenreDto[]>();

        // The union of /genre/movie/list and /genre/tv/list, name-ordered.
        Assert.Equal(
            ["Action & Adventure", "Adventure", "Drama", "Sci-Fi & Fantasy", "Science Fiction"],
            list.Select(g => g.Name));

        // The ids overlap where the names match, and `mediaTypes` is what says so.
        // Drama is 18 on both lists; a TV genre covers seasons too, because a
        // season's genres are its series' genres.
        Assert.Equal(
            ["movie", "series", "season"],
            list.Single(g => g.Name == "Drama").MediaTypes);
        Assert.Equal(["movie"], list.Single(g => g.Name == "Science Fiction").MediaTypes);
        Assert.Equal(
            ["series", "season"],
            list.Single(g => g.Name == "Action & Adventure").MediaTypes);
    }

    [Fact]
    public async Task Genres_answers_even_when_TMDB_was_never_reachable()
    {
        var fake = new FakeTmdbClient { Throw = true };
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        var response = await client.GetAsync("/api/genres");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadAsAsync<GenreDto[]>());
    }

    [Fact]
    public async Task Discover_serves_a_known_feed_and_404s_an_unknown_one()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        foreach (var feed in new[] { "popular", "top-rated", "now-playing" })
        {
            var ok = await client.GetAsync($"/api/titles/discover/{feed}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Single((await ok.ReadAsAsync<FilmPageDto>()).Results);
        }

        Assert.Equal(3, fake.DiscoverCalls);

        // FR-B4: each (feed, page) is cached, so a repeat costs nothing upstream.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/titles/discover/popular")).StatusCode);
        Assert.Equal(3, fake.DiscoverCalls);

        var unknown = await client.GetAsync("/api/titles/discover/nonsense");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("not_found", (await unknown.ReadApiErrorAsync()).Code);
        Assert.Equal(3, fake.DiscoverCalls);
    }

    [Fact]
    public async Task A_film_TMDB_does_not_have_is_404_not_503()
    {
        var fake = new FakeTmdbClient();
        var (factory, client) = await SignedInAsync(fake);
        using var _ = factory;
        using var __ = client;

        var response = await client.GetAsync($"/api/titles/{TestApi.Movie(FakeTmdbClient.UnknownToTmdbId)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
    }

    public static TheoryData<string, string> CatalogRoutes => new()
    {
        { "GET", "/api/titles/search?q=dune" },
        { "GET", "/api/titles/discover/popular" },
        { "GET", "/api/titles/movie-438631" },
        { "POST", "/api/titles/movie-438631/refresh" },
        { "GET", "/api/genres" },
    };

    [Theory]
    [MemberData(nameof(CatalogRoutes))]
    public async Task Catalog_routes_require_a_session(string method, string route)
    {
        var fake = new FakeTmdbClient();
        using var factory = new WopcornApiFactory { TmdbClient = fake };
        using var client = factory.CreateAnonymousClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.ReadApiErrorAsync()).Code);
        // Authorization runs before the action: nothing reached TMDB.
        Assert.Equal(0, fake.TotalCalls);
    }
}
