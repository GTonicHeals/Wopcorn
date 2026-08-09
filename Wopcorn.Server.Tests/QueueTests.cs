using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>API-CONTRACT.md "Queue ordering" — be-03 task 4 (FR-D1..FR-D5).</summary>
public class QueueTests
{
    [Fact]
    public async Task Order_rewrites_stored_positions_and_echoes_the_result()
    {
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in new[] { ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        var response = await world.Client.ReorderQueueAsync(
            ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var echoed = await response.ReadAsAsync<QueueOrderDto>();
        Assert.Equal(ListWorld.Keys(ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner), echoed.Keys);

        await world.AssertStoredQueueAsync(ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner);

        var page = await world.Client.GetListAsync("queue");
        Assert.Equal(
            [ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner],
            page.Entries.Select(e => e.Title.TmdbId));
    }

    [Fact]
    public async Task Order_with_a_set_that_does_not_match_is_409_carrying_the_authoritative_order()
    {
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in new[] { ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        int[][] mismatches =
        [
            [ListWorld.Alien, ListWorld.BladeRunner],                                  // one missing
            [ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Doubt], // one extra
            [ListWorld.Alien, ListWorld.BladeRunner, ListWorld.BladeRunner],           // a duplicate
            [ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Doubt],                 // right size, wrong member
            [],                                                                        // nothing at all
        ];

        foreach (var submitted in mismatches)
        {
            using var response = await world.Client.ReorderQueueAsync(submitted);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.ReadAsAsync<QueueOutOfSyncDto>();
            Assert.Equal("queue_out_of_sync", body.Code);
            Assert.False(string.IsNullOrWhiteSpace(body.Message));
            // FR-D5: the client can reconcile because the real order came back.
            Assert.Equal(ListWorld.Keys(ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact), body.Keys);
        }

        // Nothing was written on any of those attempts.
        await world.AssertStoredQueueAsync(ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact);
    }

    [Fact]
    public async Task Sort_rewrites_stored_positions_and_a_hand_reorder_still_works()
    {
        // FR-D3 and FR-D4 in one test.
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in new[]
                 { ListWorld.Contact, ListWorld.Alien, ListWorld.Doubt, ListWorld.BladeRunner })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        var sorted = await world.Client.SortQueueAsync("title");
        Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);

        var byTitle = await sorted.ReadAsAsync<QueueOrderDto>();
        Assert.Equal(ListWorld.Keys(ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Doubt), byTitle.Keys);

        // A preset is a write, not a view: the stored positions moved.
        await world.AssertStoredQueueAsync(
            ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Doubt);

        // Positions are just integers afterwards, so a hand drag still sticks.
        var reordered = await world.Client.ReorderQueueAsync(
            ListWorld.Doubt, ListWorld.Alien, ListWorld.Contact, ListWorld.BladeRunner);
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);

        await world.AssertStoredQueueAsync(
            ListWorld.Doubt, ListWorld.Alien, ListWorld.Contact, ListWorld.BladeRunner);

        var page = await world.Client.GetListAsync("queue");
        Assert.Equal(
            [ListWorld.Doubt, ListWorld.Alien, ListWorld.Contact, ListWorld.BladeRunner],
            page.Entries.Select(e => e.Title.TmdbId));
    }

    [Fact]
    public async Task Sort_presets_use_the_same_direction_defaults_and_nulls_last_rule()
    {
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in ListWorld.All)
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        // `score` defaults to desc; Ember has none and goes last either way.
        var byScore = await (await world.Client.SortQueueAsync("score")).ReadAsAsync<QueueOrderDto>();
        Assert.Equal(ListWorld.Keys(ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Doubt, ListWorld.Ember), byScore.Keys);

        // `runtime` defaults to asc, and Ember is still last.
        var byRuntime = await (await world.Client.SortQueueAsync("runtime")).ReadAsAsync<QueueOrderDto>();
        Assert.Equal(ListWorld.Keys(ListWorld.Doubt, ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Ember), byRuntime.Keys);

        await world.AssertStoredQueueAsync(
            ListWorld.Doubt, ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact, ListWorld.Ember);
    }

    [Fact]
    public async Task An_unknown_preset_falls_back_to_added_rather_than_400()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("queue", ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact);

        var response = await world.Client.SortQueueAsync("nonsense");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // `added` is desc by default — newest first.
        var order = await response.ReadAsAsync<QueueOrderDto>();
        Assert.Equal(ListWorld.Keys(ListWorld.Contact, ListWorld.BladeRunner, ListWorld.Alien), order.Keys);
    }

    [Fact]
    public async Task The_queue_view_ignores_sort_and_always_uses_the_stored_order()
    {
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in new[] { ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        // FR-D1: the queue's sort presets are a write, never a view parameter.
        var page = await world.Client.GetListAsync("queue", "?sort=title&dir=asc");

        Assert.Equal(
            [ListWorld.Contact, ListWorld.Alien, ListWorld.BladeRunner],
            page.Entries.Select(e => e.Title.TmdbId));
        Assert.Equal([0, 1, 2], page.Entries.Select(e => e.Position));
    }

    [Fact]
    public async Task Reordering_emits_no_activity_events()
    {
        using var world = await ListWorld.CreateAsync();
        foreach (var tmdbId in new[] { ListWorld.Alien, ListWorld.BladeRunner })
        {
            await world.Client.AddAndReadAsync("queue", tmdbId);
        }

        var before = (await world.Factory.ActivityAsync(world.UserId)).Count;

        await world.Client.SortQueueAsync("title");
        await world.Client.ReorderQueueAsync(ListWorld.BladeRunner, ListWorld.Alien);

        // A position is private, not feed-worthy.
        Assert.Equal(before, (await world.Factory.ActivityAsync(world.UserId)).Count);
    }

    [Fact]
    public async Task Sorting_an_empty_queue_is_an_empty_order_not_an_error()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.SortQueueAsync("title");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.ReadAsAsync<QueueOrderDto>()).Keys);
    }
}
