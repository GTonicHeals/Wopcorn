using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Plan 09, task 4. The warmer exists because availability is fetched when a title
/// is opened and nobody opens the titles already in their queue.
///
/// It is driven directly here rather than by waiting on the hosted service: the
/// interval is fifteen minutes and the startup delay is thirty seconds, neither of
/// which a test should sit through. What is asserted is the working set and the
/// failure contract; the scheduling around them is a <c>Task.Delay</c>.
/// </summary>
public class AvailabilityWarmerTests
{
    private const int Netflix = FakeTmdbClient.NetflixId;

    [Fact]
    public async Task It_warms_queued_titles_and_leaves_watched_ones_alone()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));
        await world.Client.AddAndReadAsync("watched", TestApi.Movie(AvailabilityWorld.Nowhere));

        await WarmAsync(world);

        var warmed = await StoredKeysAsync(world);

        // Watched is the largest list and the one nobody needs availability for.
        Assert.Equal([TestApi.Movie(AvailabilityWorld.Sicario)], warmed);
    }

    [Fact]
    public async Task It_warms_the_watchlist_too()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.AddAndReadAsync("watchlist", TestApi.Movie(AvailabilityWorld.Sicario));

        await WarmAsync(world);

        Assert.Equal([TestApi.Movie(AvailabilityWorld.Sicario)], await StoredKeysAsync(world));
    }

    [Fact]
    public async Task A_queued_season_warms_its_series_once()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.AddAndReadAsync("queue", TestApi.Season(FakeTmdbClient.CollisionId, 1));
        await world.Client.AddAndReadAsync("queue", TestApi.Season(FakeTmdbClient.CollisionId, 2));

        var before = world.Tmdb.WatchProviderCalls;
        await WarmAsync(world);

        // Two seasons of one show is one request, not two.
        Assert.Equal(before + 1, world.Tmdb.WatchProviderCalls);
        Assert.Equal([TestApi.Series(FakeTmdbClient.CollisionId)], await StoredKeysAsync(world));
    }

    [Fact]
    public async Task A_user_with_no_region_contributes_nothing_to_the_working_set()
    {
        using var world = await AvailabilityWorld.CreateAsync();

        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        var before = world.Tmdb.WatchProviderCalls;
        await WarmAsync(world);

        Assert.Equal(before, world.Tmdb.WatchProviderCalls);
        Assert.Empty(await StoredKeysAsync(world));
    }

    [Fact]
    public async Task A_fresh_answer_is_not_re_fetched()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));
        await WarmAsync(world);

        var after = world.Tmdb.WatchProviderCalls;
        await WarmAsync(world);

        Assert.Equal(after, world.Tmdb.WatchProviderCalls);
    }

    [Fact]
    public async Task An_outage_leaves_the_stored_answer_alone_and_does_not_throw()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);
        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        world.Tmdb.Throw = true;

        // The app is fully usable with no availability data at all, so a warming
        // failure must be a log line and nothing more.
        await WarmAsync(world);

        Assert.Empty(await StoredKeysAsync(world));
    }

    /// <summary>
    /// One pass, without the scheduling. Mirrors what
    /// <see cref="AvailabilityWarmer"/> does per title, including opening a scope
    /// per refresh — <c>BackgroundService</c> is a singleton and the context is
    /// scoped, which is the trap this shape exists to avoid.
    /// </summary>
    private static async Task WarmAsync(AvailabilityWorld world)
    {
        var scopes = world.Factory.Services.GetRequiredService<IServiceScopeFactory>();

        List<(TitleKey Key, string Region)> batch;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WopcornDbContext>();
            batch = await AvailabilityWarmer.NextBatchAsync(db, CancellationToken.None);
        }

        foreach (var (key, region) in batch)
        {
            // A scope per title, exactly as the warmer opens one.
            using var scope = scopes.CreateScope();
            var availability = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
            await availability.RefreshAsync(key, region, CancellationToken.None);
        }
    }

    private static Task<List<string>> StoredKeysAsync(AvailabilityWorld world) =>
        world.Factory.QueryAsync(db => db.TitleAvailability
            .AsNoTracking()
            .Select(a => a.TitleKey)
            .Distinct()
            .OrderBy(k => k)
            .ToListAsync());
}
