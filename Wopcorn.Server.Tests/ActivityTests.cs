using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>
/// be-03 task 6. Nothing here is exposed over HTTP yet — be-04 reads these rows —
/// but FR-G7 ("items vanish when the underlying action is undone") is a property
/// of <i>write</i> time, so it has to be correct now.
/// </summary>
public class ActivityTests
{
    [Theory]
    [InlineData("watched", ActivityKind.Watched)]
    [InlineData("watchlist", ActivityKind.AddedWatchlist)]
    [InlineData("queue", ActivityKind.AddedQueue)]
    public async Task Adding_emits_one_event_and_removing_retracts_it(string list, ActivityKind kind)
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.AddAndReadAsync(list, ListWorld.Alien);

        var emitted = Assert.Single(await world.Factory.ActivityAsync(world.UserId));
        Assert.Equal(kind, emitted.Kind);
        Assert.Equal(TestApi.Movie(ListWorld.Alien), emitted.TitleKey);
        Assert.Equal(world.UserId, emitted.UserId);
        Assert.Null(emitted.Rating);
        Assert.NotEqual(default, emitted.OccurredAt);

        // An idempotent repeat-add emits nothing new.
        await world.Client.AddAndReadAsync(list, ListWorld.Alien);
        Assert.Single(await world.Factory.ActivityAsync(world.UserId));

        await world.Client.RemoveFromListAsync(list, ListWorld.Alien);
        Assert.Empty(await world.Factory.ActivityAsync(world.UserId));
    }

    [Fact]
    public async Task Rating_emits_watched_and_rated_and_clearing_retracts_only_rated()
    {
        using var world = await ListWorld.CreateAsync();

        await world.Client.RateAsync(ListWorld.Alien, 7);

        // Ordered by kind: Rated (1) then Watched (2).
        var events = await world.Factory.ActivityAsync(world.UserId);
        Assert.Equal(["Rated:7", "Watched:"], events.Select(e => $"{e.Kind}:{e.Rating}"));

        await world.Client.ClearRatingAsync(ListWorld.Alien);

        // FR-E4: the watched entry survives, so its feed item must survive too.
        var remaining = await world.Factory.ActivityAsync(world.UserId);
        Assert.Equal(ActivityKind.Watched, Assert.Single(remaining).Kind);
    }

    [Fact]
    public async Task Removing_a_rated_film_from_watched_retracts_both_of_its_events()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.RateAsync(ListWorld.Alien, 7);

        await world.Client.RemoveFromListAsync("watched", ListWorld.Alien);

        // The rating went with the row; its feed item cannot outlive it.
        Assert.Empty(await world.Factory.ActivityAsync(world.UserId));
    }

    [Fact]
    public async Task An_add_and_its_undo_leave_the_event_table_exactly_as_it_was()
    {
        // The plan's own verification step, as a test.
        using var world = await ListWorld.CreateAsync();
        await world.Client.AddAndReadAsync("watched", ListWorld.Contact);

        var before = await CountAsync(world);

        await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);
        Assert.Equal(before + 1, await CountAsync(world));

        await world.Client.RemoveFromListAsync("watchlist", ListWorld.Alien);
        Assert.Equal(before, await CountAsync(world));
    }

    [Fact]
    public async Task alsoRemoveFrom_retracts_the_events_of_the_lists_it_clears()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);
        await world.Client.AddAndReadAsync("queue", ListWorld.Alien);
        Assert.Equal(2, (await world.Factory.ActivityAsync(world.UserId)).Count);

        await world.Client.AddToListAsync(
            "watched", ListWorld.Alien, new { alsoRemoveFrom = new[] { "watchlist", "queue" } });

        var remaining = Assert.Single(await world.Factory.ActivityAsync(world.UserId));
        Assert.Equal(ActivityKind.Watched, remaining.Kind);
    }

    [Fact]
    public async Task Events_are_scoped_to_the_user_who_acted()
    {
        using var world = await ListWorld.CreateAsync();
        await world.Client.AddAndReadAsync("watchlist", ListWorld.Alien);

        var (other, otherId) = await world.SignInAsync("bystander");
        using var _guard = other;

        await other.AddAndReadAsync("watchlist", ListWorld.Alien);

        Assert.Single(await world.Factory.ActivityAsync(world.UserId));
        Assert.Single(await world.Factory.ActivityAsync(otherId));

        // One user's undo does not touch the other's item.
        await other.RemoveFromListAsync("watchlist", ListWorld.Alien);

        Assert.Single(await world.Factory.ActivityAsync(world.UserId));
        Assert.Empty(await world.Factory.ActivityAsync(otherId));
    }

    private static Task<int> CountAsync(ListWorld world) =>
        world.Factory.QueryAsync(db => db.ActivityEvents.CountAsync());
}
