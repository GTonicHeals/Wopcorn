using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The profile payload (API-CONTRACT.md, "Profile and favourites").
///
/// The point of this suite is that <c>GET /api/me/profile</c> and
/// <c>GET /api/friends/{userId}/profile</c> are the <b>same page</b>: same DTO,
/// same numbers, differing only in <c>isSelf</c> and the taste match. Anything
/// that drifts between the two is a bug the profile screen would have to work
/// around.
/// </summary>
public class ProfileTests
{
    [Fact]
    public async Task Your_own_profile_has_no_taste_match_to_report()
    {
        using var world = await ListWorld.CreateAsync();

        var profile = await world.Client.GetMyProfileAsync();

        Assert.True(profile.IsSelf);
        Assert.Null(profile.TasteMatch);
        Assert.Equal(world.UserId.ToString(), profile.User.Id);
    }

    [Fact]
    public async Task Counts_and_runtime_describe_the_watched_list()
    {
        using var world = await ListWorld.CreateAsync();

        // Two films with runtimes (117 + 104), one series whose episode_run_time is
        // empty, and one film that knows nothing about itself.
        await world.AddPacedAsync("watched",
            TestApi.Movie(ListWorld.Alien),
            TestApi.Movie(ListWorld.Doubt),
            TestApi.Series(ListWorld.Wander),
            TestApi.Movie(ListWorld.Ember));
        await world.AddPacedAsync("watchlist", ListWorld.Contact);

        var profile = await world.Client.GetMyProfileAsync();

        Assert.Equal(4, profile.Counts.Watched);
        Assert.Equal(1, profile.Counts.Watchlist);
        Assert.Equal(0, profile.Counts.Queue);

        // The sum is only what is known, and the rest is counted rather than
        // hidden — which is what lets the client say "at least" instead of
        // passing an understatement off as a total.
        Assert.Equal(117 + 104, profile.Runtime.Minutes);
        Assert.Equal(2, profile.Runtime.KnownTitles);
        Assert.Equal(2, profile.Runtime.UnknownTitles);
    }

    [Fact]
    public async Task Top_genres_count_the_watched_list_most_watched_first()
    {
        using var world = await ListWorld.CreateAsync();

        // Alien is sci-fi + drama, Blade Runner sci-fi only, Contact both — so
        // sci-fi leads on count rather than on a tiebreak.
        await world.AddPacedAsync("watched",
            ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact);
        // On the watchlist, not watched: taste is what you have seen.
        await world.AddPacedAsync("watchlist", ListWorld.Doubt);

        var genres = (await world.Client.GetMyProfileAsync()).TopGenres;

        Assert.Equal([ListWorld.SciFi, 18], genres.Select(g => g.Id));
        Assert.Equal([3, 2], genres.Select(g => g.Count));
        Assert.All(genres, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [Fact]
    public async Task Recent_activity_is_the_owners_own_newest_first()
    {
        using var world = await ListWorld.CreateAsync();

        await world.AddPacedAsync("watchlist", ListWorld.Alien);
        await world.AddPacedAsync("watched", ListWorld.Doubt);
        Assert.Equal(HttpStatusCode.OK,
            (await world.Client.RateAsync(ListWorld.Doubt, 9)).StatusCode);

        var activity = (await world.Client.GetMyProfileAsync()).RecentActivity;

        // The feed deliberately excludes your own events; a profile is exactly
        // those events, so this is the only place you see them.
        Assert.Equal("rated", activity[0].Kind);
        Assert.Equal(9, activity[0].Rating);
        Assert.Equal(TestApi.Movie(ListWorld.Doubt), activity[0].Title.Key);
        Assert.All(activity, item => Assert.Equal(world.UserId.ToString(), item.User.Id));
    }

    [Fact]
    public async Task Undoing_an_action_takes_it_off_the_profile()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watchlist", ListWorld.Alien);

        Assert.NotEmpty((await world.Client.GetMyProfileAsync()).RecentActivity);

        await world.Client.RemoveFromListAsync("watchlist", ListWorld.Alien);

        // FR-G7, from the profile's side: the row is gone, so nothing selects it.
        Assert.Empty((await world.Client.GetMyProfileAsync()).RecentActivity);
    }

    [Fact]
    public async Task A_friends_profile_carries_their_favorites_decorated_for_you()
    {
        using var world = await SocialWorld.CreateAsync();
        var (mine, theirs) = await world.JoinFriendsAsync("mine", "theirs");

        await theirs.Client.AddAndReadAsync("watched", SocialWorld.Key(0));
        await theirs.Client.AddAndReadAsync("watched", SocialWorld.Key(1));
        await theirs.Client.RateAsync(SocialWorld.Key(0), 10);
        await theirs.Client.SetAndReadFavoritesAsync(SocialWorld.Key(1), SocialWorld.Key(0));

        // The same title is on *my* watchlist. The card has to describe me.
        await mine.Client.AddAndReadAsync("watchlist", SocialWorld.Key(0));

        var profile = await mine.Client.GetProfileAsync(theirs.Id);

        Assert.False(profile.IsSelf);
        Assert.NotNull(profile.TasteMatch);
        Assert.Equal([SocialWorld.Key(1), SocialWorld.Key(0)],
            profile.Favorites.Select(card => card.Key));

        var shared = profile.Favorites.Single(card => card.Key == SocialWorld.Key(0));
        Assert.True(shared.Lists.Watchlist);   // mine
        Assert.False(shared.Lists.Watched);    // theirs, and not mine
        Assert.Null(shared.MyRating);          // their 10 is not my rating
    }

    [Fact]
    public async Task Friend_count_is_the_owners_own()
    {
        using var world = await SocialWorld.CreateAsync();
        var (mine, theirs) = await world.JoinFriendsAsync("mine", "theirs");
        var third = await world.JoinAsync("third");
        await TestApi.BefriendAsync(theirs.Client, theirs.Id, third.Client, third.Id);

        Assert.Equal(2, (await mine.Client.GetProfileAsync(theirs.Id)).FriendCount);
        Assert.Equal(1, (await mine.Client.GetMyProfileAsync()).FriendCount);
    }

    [Fact]
    public async Task A_stranger_gets_403_not_a_profile()
    {
        using var world = await SocialWorld.CreateAsync();
        var mine = await world.JoinAsync("mine");
        var theirs = await world.JoinAsync("theirs");

        await theirs.Client.AddAndReadAsync("watched", SocialWorld.Key(0));
        await theirs.Client.SetAndReadFavoritesAsync(SocialWorld.Key(0));

        var response = await mine.Client.GetAsync($"/api/friends/{theirs.Id}/profile");

        // Favourites are visible to friends and nowhere else — the whole payload
        // is behind the same check every other friend-scoped read uses (NFR-4).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Unfriending_closes_the_profile_again()
    {
        using var world = await SocialWorld.CreateAsync();
        var (mine, theirs) = await world.JoinFriendsAsync("mine", "theirs");

        Assert.False((await mine.Client.GetProfileAsync(theirs.Id)).IsSelf);

        await mine.Client.UnfriendAsync(theirs.Id);

        var response = await mine.Client.GetAsync($"/api/friends/{theirs.Id}/profile");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Both_profile_routes_agree_about_the_same_person()
    {
        using var world = await SocialWorld.CreateAsync();
        var (mine, theirs) = await world.JoinFriendsAsync("mine", "theirs");

        await theirs.Client.AddAndReadAsync("watched", SocialWorld.Key(2));
        await theirs.Client.RateAsync(SocialWorld.Key(2), 7);
        await theirs.Client.SetAndReadFavoritesAsync(SocialWorld.Key(2));

        var own = await theirs.Client.GetMyProfileAsync();
        var seen = await mine.Client.GetProfileAsync(theirs.Id);

        // One profile screen, one payload. Only the two social fields differ.
        Assert.Equal(own.User.DisplayName, seen.User.DisplayName);
        Assert.Equal(own.Counts.Watched, seen.Counts.Watched);
        Assert.Equal(own.Stats.Count, seen.Stats.Count);
        Assert.Equal(own.Runtime.Minutes, seen.Runtime.Minutes);
        Assert.Equal(own.Favorites.Select(c => c.Key), seen.Favorites.Select(c => c.Key));
        Assert.Equal(own.RecentActivity.Select(a => a.Id), seen.RecentActivity.Select(a => a.Id));
        Assert.Equal(own.MemberSince, seen.MemberSince);

        Assert.True(own.IsSelf);
        Assert.False(seen.IsSelf);
        Assert.Null(own.TasteMatch);
        Assert.NotNull(seen.TasteMatch);
    }

    [Fact]
    public async Task Favorites_require_a_session()
    {
        using var world = await ListWorld.CreateAsync();
        using var anonymous = world.Factory.CreateAnonymousClient();

        foreach (var response in new[]
                 {
                     await anonymous.GetAsync("/api/me/profile"),
                     await anonymous.GetAsync("/api/me/favorites"),
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
