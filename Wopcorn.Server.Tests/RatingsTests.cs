using System.Net;
using System.Net.Http.Json;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>API-CONTRACT.md "Ratings" — be-03 task 5 (FR-E1..FR-E6).</summary>
public class RatingsTests
{
    [Fact]
    public async Task Rating_a_film_not_on_watched_creates_the_watched_entry()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.RateAsync(ListWorld.Alien, 9);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);      // FR-E3
        var entry = await response.ReadAsAsync<EntryDto>();
        Assert.Equal(9, entry.Title.MyRating);
        Assert.True(entry.Title.Lists.Watched);
        Assert.Null(entry.Position);

        var page = await world.Client.GetListAsync("watched");
        Assert.Equal(1, page.Count);
        Assert.Equal(ListWorld.Alien, Assert.Single(page.Entries).Title.TmdbId);
        Assert.Equal(9, page.Entries[0].Title.MyRating);
    }

    [Fact]
    public async Task Rating_a_film_already_on_watched_keeps_its_addedAt()
    {
        using var world = await ListWorld.CreateAsync();

        var added = await world.Client.AddAndReadAsync("watched", ListWorld.Alien);
        await Task.Delay(20);

        var rated = await (await world.Client.RateAsync(ListWorld.Alien, 6)).ReadAsAsync<EntryDto>();

        Assert.Equal(added.AddedAt, rated.AddedAt);
        Assert.Equal(6, rated.Title.MyRating);
    }

    [Fact]
    public async Task Clearing_a_rating_keeps_the_watched_entry()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.RateAsync(ListWorld.Alien, 9);

        var cleared = await world.Client.ClearRatingAsync(ListWorld.Alien);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var page = await world.Client.GetListAsync("watched");
        Assert.Equal(1, page.Count);                                // FR-E4
        Assert.Null(Assert.Single(page.Entries).Title.MyRating);

        // Idempotent, whether the rating or the entry is the thing that is absent.
        Assert.Equal(HttpStatusCode.NoContent,
            (await world.Client.ClearRatingAsync(ListWorld.Alien)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await world.Client.ClearRatingAsync(ListWorld.Contact)).StatusCode);
        Assert.Equal(1, (await world.Client.GetListAsync("watched")).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public async Task A_rating_outside_one_to_ten_is_400(int rating)
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.RateAsync(ListWorld.Alien, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("Rating must be between 1 and 10 half-stars.", error.Message);
        Assert.NotNull(error.Errors);
        Assert.True(error.Errors.ContainsKey("rating"));

        // Nothing was written, and the film check never ran.
        Assert.Equal(0, (await world.Client.GetListAsync("watched")).Count);
    }

    [Fact]
    public async Task A_missing_rating_field_is_400_and_never_reaches_TMDB()
    {
        using var world = await ListWorld.CreateAsync();
        var callsBefore = world.Tmdb.TotalCalls;

        var response = await world.Client.PutAsJsonAsync(
            $"/api/titles/{TestApi.Movie(ListWorld.Alien)}/rating", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
        Assert.Equal(callsBefore, world.Tmdb.TotalCalls);
    }

    [Fact]
    public async Task Rating_a_film_TMDB_does_not_have_is_404()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.RateAsync(FakeTmdbClient.UnknownToTmdbId, 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Re_rating_replaces_the_rating_and_its_feed_item()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.RateAsync(ListWorld.Alien, 4);
        await world.Client.RateAsync(ListWorld.Alien, 9);

        Assert.Equal(9, Assert.Single((await world.Client.GetListAsync("watched")).Entries).Title.MyRating);

        // Replaced, not appended — a re-rate moves up the feed instead of doubling.
        var rated = (await world.Factory.ActivityAsync(world.UserId))
            .Where(e => e.Kind == ActivityKind.Rated)
            .ToList();
        Assert.Single(rated);
        Assert.Equal(9, rated[0].Rating);
    }

    [Fact]
    public async Task Removing_from_watched_discards_the_rating()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.RateAsync(ListWorld.Alien, 9);

        await world.Client.RemoveFromListAsync("watched", ListWorld.Alien);
        await world.Client.AddAndReadAsync("watched", ListWorld.Alien);

        Assert.Null(Assert.Single((await world.Client.GetListAsync("watched")).Entries).Title.MyRating);
    }

    [Fact]
    public async Task Rating_stats_summarise_the_watched_list()
    {
        using var world = await ListWorld.CreateAsync();

        var empty = await ReadStatsAsync(world);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.Average);                    // never a division by zero
        Assert.Equal(10, empty.Distribution.Length);
        Assert.All(empty.Distribution, bucket => Assert.Equal(0, bucket));

        await world.Client.RateAsync(ListWorld.Alien, 9);
        await world.Client.RateAsync(ListWorld.BladeRunner, 9);
        await world.Client.RateAsync(ListWorld.Contact, 6);
        // Watched but unrated, and watchlisted-and-rated-elsewhere: neither counts.
        await world.Client.AddAndReadAsync("watched", ListWorld.Doubt);
        await world.Client.AddAndReadAsync("watchlist", ListWorld.Ember);

        var stats = await ReadStatsAsync(world);
        Assert.Equal(3, stats.Count);
        Assert.Equal(8.0, stats.Average!.Value, 2);    // (9 + 9 + 6) / 3
        Assert.Equal(2, stats.Distribution[8]);        // index 0 is one half-star
        Assert.Equal(1, stats.Distribution[5]);
        Assert.Equal(3, stats.Distribution.Sum());
    }

    [Fact]
    public async Task Rating_stats_are_per_user()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.RateAsync(ListWorld.Alien, 10);

        var (other, _) = await world.SignInAsync("stranger");
        using var _guard = other;

        var response = await other.GetAsync("/api/me/rating-stats");
        var stats = await response.ReadAsAsync<RatingStatsDto>();

        Assert.Equal(0, stats.Count);
        Assert.Null(stats.Average);
    }

    private static async Task<RatingStatsDto> ReadStatsAsync(ListWorld world)
    {
        var response = await world.Client.GetAsync("/api/me/rating-stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<RatingStatsDto>();
    }
}
