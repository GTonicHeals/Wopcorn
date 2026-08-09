using System.Collections.Concurrent;
using System.Net;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>API-CONTRACT.md "Lists" — be-03 tasks 1–3 (FR-C1..FR-C7).</summary>
public class ListsTests
{
    [Fact]
    public async Task Adding_twice_returns_the_same_addedAt_and_emits_one_event()
    {
        using var world = await ListWorld.CreateAsync();

        var first = await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);
        await Task.Delay(20);
        var second = await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);

        // Idempotent: a repeat add is not a new add.
        Assert.Equal(first.AddedAt, second.AddedAt);
        Assert.Equal(1, (await world.Client.GetListAsync("watchlist")).Count);

        var events = await world.Factory.ActivityAsync(world.UserId);
        Assert.Equal(ActivityKind.AddedWatchlist, Assert.Single(events).Kind);
    }

    [Fact]
    public async Task Removing_an_absent_entry_is_204()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.RemoveFromListAsync("queue", ListWorld.Alien);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await world.Factory.ActivityAsync(world.UserId));
    }

    [Fact]
    public async Task Removing_the_middle_of_a_three_item_queue_leaves_stored_positions_0_and_1()
    {
        using var world = await ListWorld.CreateAsync();

        foreach (var tmdbId in new[] { ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        await world.AssertStoredQueueAsync(ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact);

        var removed = await world.Client.RemoveFromListAsync("queue", ListWorld.BladeRunner);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // The obligation is about stored state, which a response body cannot prove:
        // the survivors must be renumbered, not merely returned in order.
        await world.AssertStoredQueueAsync(ListWorld.Alien, ListWorld.Contact);
    }

    [Fact]
    public async Task A_new_queue_entry_appends_to_the_end()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.AddAndReadAsync("queue", ListWorld.Alien);
        var second = await world.Client.AddAndReadAsync("queue", ListWorld.BladeRunner);

        Assert.Equal(1, second.Position);       // FR-D1

        // And after a compaction, the next add still lands at the end.
        await world.Client.RemoveFromListAsync("queue", ListWorld.Alien);
        var third = await world.Client.AddAndReadAsync("queue", ListWorld.Contact);

        Assert.Equal(1, third.Position);
        await world.AssertStoredQueueAsync(ListWorld.BladeRunner, ListWorld.Contact);
    }

    [Fact]
    public async Task alsoRemoveFrom_moves_a_film_between_lists_in_one_round_trip()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);
        await world.Client.AddAndReadAsync("queue", ListWorld.Alien);
        await world.Client.AddAndReadAsync("queue", ListWorld.BladeRunner);

        var response = await world.Client.AddToListAsync(
            "watched",
            ListWorld.Alien,
            // "nonsense" is ignored rather than rejected.
            new { alsoRemoveFrom = new[] { "watchlist", "queue", "nonsense" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);       // FR-C6
        var entry = await response.ReadAsAsync<EntryDto>();
        Assert.True(entry.Title.Lists.Watched);
        Assert.False(entry.Title.Lists.Watchlist);
        Assert.False(entry.Title.Lists.Queue);

        Assert.Equal(0, (await world.Client.GetListAsync("watchlist")).Count);
        // The queue compacted around the removal in the same transaction.
        await world.AssertStoredQueueAsync(ListWorld.BladeRunner);

        var events = await world.Factory.ActivityAsync(world.UserId);
        Assert.Equal(
            [$"Watched:{TestApi.Movie(ListWorld.Alien)}", $"AddedQueue:{TestApi.Movie(ListWorld.BladeRunner)}"],
            events.Select(e => $"{e.Kind}:{e.TitleKey}"));
    }

    [Fact]
    public async Task The_three_lists_are_independent()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);
        await world.Client.AddAndReadAsync("watched", ListWorld.Alien);

        // Watching something does not implicitly clear the watchlist unless the
        // caller asked for it (glossary §1).
        Assert.Equal(1, (await world.Client.GetListAsync("watchlist")).Count);
        Assert.Equal(1, (await world.Client.GetListAsync("watched")).Count);

        var card = Assert.Single((await world.Client.GetListAsync("watched")).Entries).Title;
        Assert.True(card.Lists.Watched);
        Assert.True(card.Lists.Watchlist);
        Assert.False(card.Lists.Queue);
    }

    [Fact]
    public async Task An_unknown_list_name_is_404_on_every_verb()
    {
        using var world = await ListWorld.CreateAsync();

        HttpResponseMessage[] responses =
        [
            await world.Client.GetAsync("/api/lists/nonsense"),
            await world.Client.AddToListAsync("nonsense", ListWorld.Alien),
            await world.Client.RemoveFromListAsync("nonsense", ListWorld.Alien),
        ];

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
            response.Dispose();
        }
    }

    [Fact]
    public async Task Sorting_and_filtering_are_applied_in_the_database()
    {
        var log = new ConcurrentQueue<string>();
        using var world = await ListWorld.CreateAsync(log);

        foreach (var tmdbId in ListWorld.All)
        {
            await world.Client.AddAndReadAsync("watched", tmdbId);
        }

        await world.Client.RateAsync(ListWorld.Alien, 8);          // 1979, sci-fi
        await world.Client.RateAsync(ListWorld.BladeRunner, 3);    // 1982, sci-fi
        await world.Client.RateAsync(ListWorld.Contact, 10);       // 1997, sci-fi
        await world.Client.RateAsync(ListWorld.Doubt, 5);          // 2008, drama

        log.Clear();

        // The plan's worked example: 80s and 90s sci-fi, by ascending rating.
        var page = await world.Client.GetListAsync(
            "watched", "?sort=rating&dir=asc&genre=878&decade=1980&decade=1990");

        // count is the unfiltered total for the list; entries are the filtered
        // result (FR-C4).
        Assert.Equal(5, page.Count);
        Assert.Equal(
            [ListWorld.BladeRunner, ListWorld.Contact],
            page.Entries.Select(e => e.Title.TmdbId));
        Assert.Equal([3, 10], page.Entries.Select(e => e.Title.MyRating));

        // In the database, not in memory.
        Assert.Contains(log, line => line.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("WHERE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nulls_sort_last_in_both_directions()
    {
        using var world = await ListWorld.CreateAsync();

        foreach (var tmdbId in ListWorld.All)
        {
            await world.Client.AddAndReadAsync("watched", tmdbId);
        }

        // Runtimes: Doubt 104, Alien 117, Blade Runner 117, Contact 150, Ember none.
        var ascending = await world.Client.GetListAsync("watched", "?sort=runtime&dir=asc");
        Assert.Equal(
            [ListWorld.Doubt, ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Ember],
            ascending.Entries.Select(e => e.Title.TmdbId));

        var descending = await world.Client.GetListAsync("watched", "?sort=runtime&dir=desc");
        Assert.Equal(
            [ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Doubt, ListWorld.Ember],
            descending.Entries.Select(e => e.Title.TmdbId));
    }

    [Fact]
    public async Task Sort_defaults_follow_the_contract()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.Contact, ListWorld.Alien, ListWorld.Doubt);

        // `added` defaults to desc — newest first.
        Assert.Equal(
            [ListWorld.Doubt, ListWorld.Alien, ListWorld.Contact],
            (await world.Client.GetListAsync("watched")).Entries.Select(e => e.Title.TmdbId));

        // `score` defaults to desc: Alien 8.1, Contact 7.4, Doubt 7.1.
        Assert.Equal(
            [ListWorld.Alien, ListWorld.Contact, ListWorld.Doubt],
            (await world.Client.GetListAsync("watched", "?sort=score")).Entries.Select(e => e.Title.TmdbId));

        // `title` and `year` default to asc.
        Assert.Equal(
            [ListWorld.Alien, ListWorld.Contact, ListWorld.Doubt],
            (await world.Client.GetListAsync("watched", "?sort=title")).Entries.Select(e => e.Title.TmdbId));
        Assert.Equal(
            [ListWorld.Alien, ListWorld.Contact, ListWorld.Doubt],
            (await world.Client.GetListAsync("watched", "?sort=year")).Entries.Select(e => e.Title.TmdbId));
    }

    [Fact]
    public async Task Unknown_sort_and_filter_values_are_ignored_not_rejected()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watchlist", ListWorld.Alien, ListWorld.BladeRunner);

        // `rating` is not a valid sort off the watched list, `sideways` is not a
        // direction, and neither filter value parses.
        var page = await world.Client.GetListAsync(
            "watchlist", "?sort=rating&dir=sideways&genre=abc&decade=notayear");

        Assert.Equal(2, page.Entries.Length);
        // Everything fell back to `added`, which is desc by default.
        Assert.Equal(
            [ListWorld.BladeRunner, ListWorld.Alien],
            page.Entries.Select(e => e.Title.TmdbId));
    }

    [Fact]
    public async Task A_film_TMDB_does_not_have_cannot_be_added()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.AddToListAsync("watchlist", FakeTmdbClient.UnknownToTmdbId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
        Assert.Equal(0, (await world.Client.GetListAsync("watchlist")).Count);
    }

    [Fact]
    public async Task With_TMDB_down_an_uncached_add_is_503_but_the_lists_still_render()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.AddAndReadAsync("watched", ListWorld.Alien);

        world.Tmdb.Throw = true;
        var callsBefore = world.Tmdb.TotalCalls;

        var blocked = await world.Client.AddToListAsync("watchlist", FakeTmdbClient.NeverCachedId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal("tmdb_unavailable", (await blocked.ReadApiErrorAsync()).Code);

        // FR-B6: rendering a list never goes upstream, outage or not.
        var page = await world.Client.GetListAsync("watched");
        Assert.Equal("Alien", Assert.Single(page.Entries).Title.Title);
        Assert.Equal(callsBefore + 1, world.Tmdb.TotalCalls);   // only the blocked write tried
    }

    [Fact]
    public async Task watchedOn_is_stored_updatable_and_meaningless_off_the_watched_list()
    {
        using var world = await ListWorld.CreateAsync();

        var first = await (await world.Client.AddToListAsync(
            "watched", ListWorld.Alien, new { watchedOn = "2024-03-01" })).ReadAsAsync<EntryDto>();
        Assert.Equal("2024-03-01", first.WatchedOn);

        // The one field a repeat add may change — AddedAt still does not move.
        var second = await (await world.Client.AddToListAsync(
            "watched", ListWorld.Alien, new { watchedOn = "2024-04-02" })).ReadAsAsync<EntryDto>();
        Assert.Equal("2024-04-02", second.WatchedOn);
        Assert.Equal(first.AddedAt, second.AddedAt);

        var queued = await (await world.Client.AddToListAsync(
            "queue", ListWorld.Alien, new { watchedOn = "2024-03-01" })).ReadAsAsync<EntryDto>();
        Assert.Null(queued.WatchedOn);      // OD-1
    }

    [Fact]
    public async Task A_two_hundred_entry_list_costs_the_same_queries_as_a_ten_entry_one()
    {
        var log = new ConcurrentQueue<string>();
        using var world = await ListWorld.CreateAsync(log);

        await SeedWatchedAsync(world, firstId: 1_000, count: 10);
        await world.Client.GetListAsync("watched");     // warm up anything one-time
        log.Clear();

        Assert.Equal(10, (await world.Client.GetListAsync("watched")).Count);
        var small = world.Factory.SqlCommandCount;

        await SeedWatchedAsync(world, firstId: 2_000, count: 200);
        log.Clear();

        var page = await world.Client.GetListAsync("watched");
        var large = world.Factory.SqlCommandCount;

        Assert.Equal(210, page.Count);
        Assert.Equal(210, page.Entries.Length);

        // Guard against a vacuous pass: the log must actually be recording, and a
        // list view must cost a small handful of queries rather than none.
        Assert.InRange(small, 1, 10);
        // NFR-2: constant, not proportional. Twenty times the rows, same cost.
        Assert.Equal(small, large);
    }

    /// <summary>Writes film and entry rows straight to the database — no HTTP, no TMDB.</summary>
    private static Task SeedWatchedAsync(ListWorld world, int firstId, int count) =>
        world.Factory.QueryAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;

            for (var i = 0; i < count; i++)
            {
                var tmdbId = firstId + i;
                var key = TitleKey.ForMovie(tmdbId);

                var title = Title.New(key, $"Film {tmdbId}", now);
                db.Titles.Add(title);

                db.ListEntries.Add(new ListEntry
                {
                    UserId = world.UserId,
                    TitleKey = key.Value,
                    Kind = ListKind.Watched,
                    AddedAt = now,
                });
            }

            return await db.SaveChangesAsync();
        });
}
