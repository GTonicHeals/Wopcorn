using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Plan 08 — the list and queue behaviour that only shows up once a list can hold
/// more than one kind of thing.
///
/// <see cref="ListWorld"/> supplies the catalogue: five films, a series with a
/// known episode length (<see cref="ListWorld.Beacon"/>), and a series whose
/// <c>episode_run_time</c> is empty (<see cref="ListWorld.Wander"/>) — which is
/// the ordinary case, not the exceptional one.
/// </summary>
public class MixedListTests
{
    private static string Beacon => TestApi.Series(ListWorld.Beacon);
    private static string Wander => TestApi.Series(ListWorld.Wander);
    private static string BeaconSeasonOne => TestApi.Season(ListWorld.Beacon, 1);

    // --- the queue ----------------------------------------------------------

    [Fact]
    public async Task A_queue_holding_a_film_a_series_and_a_season_reorders_across_all_three()
    {
        using var world = await ListWorld.CreateAsync();

        // The season row has to exist before it can be queued, and opening the
        // series is what creates it.
        await world.Client.GetTitleAsync(Beacon);

        var film = TestApi.Movie(ListWorld.Alien);
        await world.AddPacedAsync("queue", film, Beacon, BeaconSeasonOne);

        await world.AssertStoredQueueAsync(film, Beacon, BeaconSeasonOne);

        // Reversed, which no single-type queue could express.
        var response = await world.Client.ReorderQueueAsync(BeaconSeasonOne, Beacon, film);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var echoed = await response.ReadAsAsync<QueueOrderDto>();
        Assert.Equal([BeaconSeasonOne, Beacon, film], echoed.Keys);

        await world.AssertStoredQueueAsync(BeaconSeasonOne, Beacon, film);

        // And the view agrees, with each entry still knowing what it is.
        var page = await world.Client.GetListAsync("queue");
        Assert.Equal([BeaconSeasonOne, Beacon, film], page.Entries.Select(e => e.Title.Key));
        Assert.Equal(["season", "series", "movie"], page.Entries.Select(e => e.Title.MediaType));
    }

    [Fact]
    public async Task A_mixed_queue_rejects_an_order_that_is_not_exactly_its_membership()
    {
        using var world = await ListWorld.CreateAsync();

        var film = TestApi.Movie(ListWorld.Alien);
        await world.AddPacedAsync("queue", film, Beacon);

        // A key that is not in the queue — even a legal one — is out of sync.
        var response = await world.Client.ReorderQueueAsync(film, Beacon, Wander);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsAsync<QueueOutOfSyncDto>();
        Assert.Equal("queue_out_of_sync", body.Code);
        Assert.Equal([film, Beacon], body.Keys);

        await world.AssertStoredQueueAsync(film, Beacon);
    }

    // --- the type filter ----------------------------------------------------

    [Fact]
    public async Task The_type_filter_narrows_the_entries_but_leaves_count_unfiltered()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.GetTitleAsync(Beacon);

        var alien = TestApi.Movie(ListWorld.Alien);
        var contact = TestApi.Movie(ListWorld.Contact);
        await world.AddPacedAsync("watchlist", alien, contact, Beacon, BeaconSeasonOne);

        var all = await world.Client.GetListAsync("watchlist");
        Assert.Equal(4, all.Count);
        Assert.Equal(4, all.Entries.Length);

        var seriesOnly = await world.Client.GetListAsync("watchlist", "?type=series");
        Assert.Equal([Beacon], seriesOnly.Entries.Select(e => e.Title.Key));
        // `count` is deliberately the unfiltered total, so the header can say
        // "showing 1 of 4" from this one request (FR-C4).
        Assert.Equal(4, seriesOnly.Count);

        // Repeatable, and the two combine as a union.
        var both = await world.Client.GetListAsync("watchlist", "?type=series&type=season");
        Assert.Equal([Beacon, BeaconSeasonOne], both.Entries.Select(e => e.Title.Key).Order());
        Assert.Equal(4, both.Count);

        var films = await world.Client.GetListAsync("watchlist", "?type=movie");
        Assert.Equal([alien, contact], films.Entries.Select(e => e.Title.Key).Order());
        Assert.Equal(4, films.Count);
    }

    [Fact]
    public async Task An_unknown_type_value_is_ignored_rather_than_rejected()
    {
        using var world = await ListWorld.CreateAsync();

        await world.AddPacedAsync("watchlist", ListWorld.Alien);

        // Same rule as sort and genre: a stale bookmark still renders (FR-C5).
        var page = await world.Client.GetListAsync("watchlist", "?type=episode");
        Assert.Equal(1, page.Count);
        Assert.Single(page.Entries);
    }

    // --- null runtimes ------------------------------------------------------

    [Fact]
    public async Task Series_with_no_runtime_sort_last_in_both_directions()
    {
        using var world = await ListWorld.CreateAsync();

        // Beacon has a runtime (50 × 24); Wander's episode_run_time is empty, so it
        // has none at all — and neither does Ember, the film with nothing known.
        await world.AddPacedAsync(
            "watchlist",
            TestApi.Movie(ListWorld.Alien),   // 117
            TestApi.Movie(ListWorld.Ember),   // null
            Beacon,                           // 1200
            Wander);                          // null

        var ascending = await world.Client.GetListAsync("watchlist", "?sort=runtime&dir=asc");
        Assert.Equal(
            [TestApi.Movie(ListWorld.Alien), Beacon],
            ascending.Entries.Take(2).Select(e => e.Title.Key));
        Assert.Equal(
            [TestApi.Movie(ListWorld.Ember), Wander],
            ascending.Entries.Skip(2).Select(e => e.Title.Key).Order());

        // Nulls stay at the bottom descending too — "no runtime recorded" belongs
        // last whichever end the user asked for.
        var descending = await world.Client.GetListAsync("watchlist", "?sort=runtime&dir=desc");
        Assert.Equal(
            [Beacon, TestApi.Movie(ListWorld.Alien)],
            descending.Entries.Take(2).Select(e => e.Title.Key));
        Assert.Equal(
            [TestApi.Movie(ListWorld.Ember), Wander],
            descending.Entries.Skip(2).Select(e => e.Title.Key).Order());
    }

    [Fact]
    public async Task A_series_reports_its_season_count_where_a_film_reports_a_runtime()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.GetTitleAsync(Beacon);
        await world.AddPacedAsync("watchlist", Beacon, TestApi.Movie(ListWorld.Alien));

        var page = await world.Client.GetListAsync("watchlist");
        var series = page.Entries.Single(e => e.Title.Key == Beacon).Title;
        var film = page.Entries.Single(e => e.Title.MediaType == "movie").Title;

        Assert.Equal(3, series.SeasonCount);
        Assert.Equal(24, series.EpisodeCount);

        // A film has neither, which is what lets the card pick its meta line.
        Assert.Null(film.SeasonCount);
        Assert.Null(film.EpisodeCount);
        Assert.Equal(117, film.RuntimeMinutes);
    }

    // --- the feed -----------------------------------------------------------

    [Fact]
    public async Task The_feed_carries_a_series_and_a_season_the_same_way_it_carries_a_film()
    {
        using var world = await SocialWorld.CreateAsync();

        world.Tmdb.WithSeries(ListWorld.Beacon, "Beacon", "2011-04-17", 8.4,
            episodeRunTime: 50, seasonEpisodes: [10, 10, 4], genreIds: 18);

        var (me, them) = await world.JoinFriendsAsync("ada", "rex");

        await them.Client.GetTitleAsync(Beacon);
        await them.Client.AddAndReadAsync("watched", Beacon);
        await them.Client.AddAndReadAsync("watched", BeaconSeasonOne);

        var feed = await me.Client.GetFeedAsync();

        // `kind` is unchanged across media types — "watched" reads right for all
        // three — and the card is what says which one this was.
        Assert.Equal(2, feed.Items.Length);
        Assert.All(feed.Items, item => Assert.Equal("watched", item.Kind));
        Assert.Equal(
            [Beacon, BeaconSeasonOne],
            feed.Items.Select(i => i.Title.Key).Order());
        Assert.Contains(feed.Items, i => i.Title.MediaType == "series");
        Assert.Contains(feed.Items, i => i.Title.MediaType == "season");
    }
}
