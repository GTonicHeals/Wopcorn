using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The friends feed (FR-G2, FR-G3, FR-G7) — API-CONTRACT.md "Feed".
///
/// The pagination test is the point of this file. Keyset paging is easy to write
/// in a way that looks right on page one and silently duplicates or drops rows at
/// a boundary, so it is checked by walking the whole history and comparing the
/// union against what was seeded.
/// </summary>
public class FeedTests
{
    /// <summary>Sixty events: thirty watchlist adds each, across two friends.</summary>
    private const int EventsPerFriend = 30;

    [Fact]
    public async Task Feed_pages_by_keyset_with_no_duplicates_gaps_or_offset()
    {
        var sql = new ConcurrentQueue<string>();
        using var world = await SocialWorld.CreateAsync(sql);

        var me = await world.JoinAsync("viewer");
        var (first, second) = await SeedTwoFriendsAsync(world, me);

        sql.Clear();

        var pages = new List<FeedPageDto>();
        string? cursor = null;

        do
        {
            var query = cursor is null
                ? "?limit=20"
                : $"?limit=20&cursor={Uri.EscapeDataString(cursor)}";

            var page = await me.Client.GetFeedAsync(query);
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null && pages.Count < 10);   // guard against a cursor that never ends

        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.Equal(20, p.Items.Length));
        Assert.Null(pages[^1].NextCursor);

        var ids = pages.SelectMany(p => p.Items.Select(i => i.Id)).ToList();

        // No duplicates and no gaps: the union is exactly the seeded history.
        Assert.Equal(60, ids.Count);
        Assert.Equal(60, ids.Distinct().Count());

        var stored = await world.Factory.QueryAsync(db => db.ActivityEvents
            .AsNoTracking()
            .Where(a => a.UserId == first.Id || a.UserId == second.Id)
            .Select(a => a.Id)
            .ToListAsync());

        Assert.Equal(stored.Select(id => id.ToString()).OrderBy(x => x), ids.OrderBy(x => x));

        // Newest first, all the way across the page boundaries.
        var times = pages.SelectMany(p => p.Items.Select(i => i.OccurredAt)).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        // FR-G3: keyset, never OFFSET over full history.
        Assert.DoesNotContain(sql, line => line.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sql, line => line.Contains("Executed DbCommand", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Malformed_cursor_is_400_not_500()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("viewer");

        foreach (var cursor in new[] { "not-base64!!", "Zm9v", "!!!!", "e30" })
        {
            var response = await me.Client.GetAsync($"/api/feed?cursor={Uri.EscapeDataString(cursor)}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
        }
    }

    [Fact]
    public async Task Unfriending_removes_that_users_items_from_page_one()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var (first, second) = await SeedTwoFriendsAsync(world, me);

        var before = await me.Client.GetFeedAsync("?limit=50");
        Assert.Contains(before.Items, i => i.User.Id == first.Id.ToString());
        Assert.Contains(before.Items, i => i.User.Id == second.Id.ToString());

        var removed = await me.Client.UnfriendAsync(first.Id);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var after = await me.Client.GetFeedAsync("?limit=50");
        Assert.DoesNotContain(after.Items, i => i.User.Id == first.Id.ToString());
        Assert.Contains(after.Items, i => i.User.Id == second.Id.ToString());
    }

    [Fact]
    public async Task Feed_is_friends_only_and_excludes_your_own_activity()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        var stranger = await world.JoinAsync("stranger");

        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);

        await me.Client.AddAndReadAsync("watchlist", SocialWorld.Film(0));
        await friend.Client.AddAndReadAsync("watchlist", SocialWorld.Film(1));
        await stranger.Client.AddAndReadAsync("watchlist", SocialWorld.Film(2));

        var feed = await me.Client.GetFeedAsync();

        Assert.Single(feed.Items);
        Assert.Equal(friend.Id.ToString(), feed.Items[0].User.Id);
        Assert.Equal(SocialWorld.Film(1), feed.Items[0].Title.TmdbId);
        Assert.Equal("added_watchlist", feed.Items[0].Kind);
        Assert.Null(feed.Items[0].Rating);
    }

    [Fact]
    public async Task A_rating_shows_its_value_and_undoing_it_removes_the_item()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);

        await friend.Client.RateAsync(SocialWorld.Film(0), 9);

        var feed = await me.Client.GetFeedAsync();

        // Rating emits two events: the implied Watched and the Rated (be-03).
        var rated = Assert.Single(feed.Items, i => i.Kind == "rated");
        Assert.Equal(9, rated.Rating);
        Assert.Contains(feed.Items, i => i.Kind == "watched");

        // FR-G7: undo removes it, with no tombstone left behind.
        await friend.Client.ClearRatingAsync(SocialWorld.Film(0));

        var after = await me.Client.GetFeedAsync();
        Assert.DoesNotContain(after.Items, i => i.Kind == "rated");
        Assert.Contains(after.Items, i => i.Kind == "watched");

        await friend.Client.RemoveFromListAsync("watched", SocialWorld.Film(0));

        Assert.Empty((await me.Client.GetFeedAsync()).Items);
    }

    [Fact]
    public async Task Feed_decorates_films_with_the_viewers_own_membership()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);

        await friend.Client.RateAsync(SocialWorld.Film(0), 4);
        await me.Client.AddAndReadAsync("watchlist", SocialWorld.Film(0));
        await me.Client.RateAsync(SocialWorld.Film(0), 8);

        var rated = Assert.Single((await me.Client.GetFeedAsync()).Items, i => i.Kind == "rated");

        Assert.Equal(4, rated.Rating);              // the friend's rating, on the item
        Assert.Equal(8, rated.Title.MyRating);       // mine, on the card
        Assert.True(rated.Title.Lists.Watchlist);
        Assert.True(rated.Title.Lists.Watched);
    }

    [Fact]
    public async Task Feed_renders_during_a_tmdb_outage()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);
        await friend.Client.AddAndReadAsync("watchlist", SocialWorld.Film(0));

        world.Tmdb.Throw = true;
        var upstreamCallsBefore = world.Tmdb.TotalCalls;

        var feed = await me.Client.GetFeedAsync();

        Assert.Single(feed.Items);
        Assert.Equal("Film 00", feed.Items[0].Title.Title);
        Assert.Equal(upstreamCallsBefore, world.Tmdb.TotalCalls);   // FR-B6, NFR-2
    }

    [Fact]
    public async Task Limit_is_clamped_rather_than_rejected()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);

        foreach (var tmdbId in SocialWorld.Films(3))
        {
            await friend.Client.AddAndReadAsync("watchlist", tmdbId);
        }

        Assert.Single((await me.Client.GetFeedAsync("?limit=0")).Items);
        Assert.Equal(3, (await me.Client.GetFeedAsync("?limit=9999")).Items.Length);
    }

    [Fact]
    public async Task An_empty_feed_has_no_cursor()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("viewer");

        var feed = await me.Client.GetFeedAsync();

        Assert.Empty(feed.Items);
        Assert.Null(feed.NextCursor);
    }

    /// <summary>
    /// Two friends of <paramref name="viewer"/>, each with
    /// <see cref="EventsPerFriend"/> watchlist adds — sixty events in total, all
    /// visible to the viewer and none of them the viewer's own.
    /// </summary>
    private static async Task<(Member First, Member Second)> SeedTwoFriendsAsync(
        SocialWorld world, Member viewer)
    {
        var first = await world.JoinAsync("alpha");
        var second = await world.JoinAsync("beta");

        await TestApi.BefriendAsync(viewer.Client, viewer.Id, first.Client, first.Id);
        await TestApi.BefriendAsync(viewer.Client, viewer.Id, second.Client, second.Id);

        for (var i = 0; i < EventsPerFriend; i++)
        {
            await first.Client.AddAndReadAsync("watchlist", SocialWorld.Film(i));
            await second.Client.AddAndReadAsync("watchlist", SocialWorld.Film(i));
        }

        return (first, second);
    }
}
