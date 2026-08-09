using System.Net;
using System.Text;

namespace Wopcorn.Server.Tests;

/// <summary>
/// NFR-4 and FR-F6: a friend-scoped read is authorized on the request that makes
/// it, and a non-friend sees only a display name and an avatar anywhere else.
///
/// Written as a loop over <b>every</b> <c>/api/friends/{userId}/…</c> route rather
/// than one worked example, for the same reason <see cref="ListOwnershipTests"/>
/// is: a route added later without the friendship gate would slip past a single
/// example. Adding a route to be-04 means adding a row here.
/// </summary>
public class FriendVisibilityTests
{
    /// <summary>Every be-04 route that reads something about another user.</summary>
    public static TheoryData<string> FriendScopedRoutes =>
    [
        "/api/friends/{userId}/profile",
        "/api/friends/{userId}/lists/watched",
        "/api/friends/{userId}/lists/watchlist",
        "/api/friends/{userId}/lists/queue",
        "/api/friends/{userId}/lists/watched?sort=rating&dir=asc&genre=878&decade=1990",
    ];

    [Theory]
    [MemberData(nameof(FriendScopedRoutes))]
    public async Task A_non_friend_gets_403(string template)
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var stranger = await world.JoinAsync("stranger");
        await stranger.Client.AddAndReadAsync("watched", SocialWorld.Film(0));

        var response = await me.Client.GetAsync(template.Replace("{userId}", stranger.Id.ToString()));

        // 403, not 404 and not an empty 200: an empty list would leak "that user
        // exists but has nothing", and a 404 would leak the opposite.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", (await response.ReadApiErrorAsync()).Code);
    }

    [Theory]
    [MemberData(nameof(FriendScopedRoutes))]
    public async Task An_accepted_friend_gets_200_and_an_unfriended_one_goes_back_to_403(string template)
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var friend = await world.JoinAsync("friend");
        await friend.Client.AddAndReadAsync("watched", SocialWorld.Film(0));

        var route = template.Replace("{userId}", friend.Id.ToString());

        // Before the handshake it is 403 …
        Assert.Equal(HttpStatusCode.Forbidden, (await me.Client.GetAsync(route)).StatusCode);

        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);
        Assert.Equal(HttpStatusCode.OK, (await me.Client.GetAsync(route)).StatusCode);

        // … and the moment the friendship goes, so does the access. No cached
        // "is a friend" anywhere.
        await me.Client.UnfriendAsync(friend.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await me.Client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(FriendScopedRoutes))]
    public async Task Your_own_id_is_not_a_friend_of_yours(string template)
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("viewer");

        var response = await me.Client.GetAsync(template.Replace("{userId}", me.Id.ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Every route be-04 adds, read and write alike.</summary>
    public static TheoryData<string, string, string?> AllRoutes
    {
        get
        {
            var routes = new TheoryData<string, string, string?>
            {
                { "GET", "/api/users/search?q=a", null },
                { "GET", "/api/friends", null },
                { "GET", "/api/feed", null },
                { "POST", "/api/friends/requests", """{"userId":"{userId}"}""" },
                { "POST", "/api/friends/requests/{userId}/accept", null },
                { "POST", "/api/friends/requests/{userId}/decline", null },
                { "DELETE", "/api/friends/requests/{userId}", null },
                { "DELETE", "/api/friends/{userId}", null },
            };

            foreach (var route in FriendScopedRoutes)
            {
                routes.Add("GET", route, null);
            }

            return routes;
        }
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_be04_route_answers_401_without_a_session(
        string method, string template, string? body)
    {
        using var factory = new WopcornApiFactory { TmdbClient = new FakeTmdbClient() };
        using var client = factory.CreateAnonymousClient();

        var id = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(
            new HttpMethod(method), template.Replace("{userId}", id));

        if (body is not null)
        {
            request.Content = new StringContent(
                body.Replace("{userId}", id), Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);      // not an HTML login redirect
        Assert.Equal("unauthenticated", (await response.ReadApiErrorAsync()).Code);
    }

    /// <summary>
    /// A pending request is not a friendship — the gate is "accepted", not "known
    /// to each other" (FR-F2).
    /// </summary>
    [Fact]
    public async Task A_pending_request_does_not_grant_access()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var other = await world.JoinAsync("other");

        await me.Client.SendFriendRequestAsync(other.Id);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await me.Client.GetAsync($"/api/friends/{other.Id}/profile")).StatusCode);
    }

    [Fact]
    public async Task A_friends_profile_carries_their_stats_and_counts()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        await friend.Client.RateAsync(SocialWorld.Film(0), 9);
        await friend.Client.RateAsync(SocialWorld.Film(1), 7);
        await friend.Client.AddAndReadAsync("watchlist", SocialWorld.Film(2));
        await friend.Client.AddAndReadAsync("queue", SocialWorld.Film(3));
        await friend.Client.AddAndReadAsync("queue", SocialWorld.Film(4));

        var profile = await (await me.Client.GetAsync($"/api/friends/{friend.Id}/profile"))
            .ReadAsAsync<FriendProfileDto>();

        Assert.Equal(friend.Id.ToString(), profile.User.Id);
        Assert.Equal(2, profile.Stats.Count);
        Assert.Equal(8.0, profile.Stats.Average!.Value, 2);
        Assert.Equal(new ListCountsDto(2, 1, 2), profile.Counts);

        // Their stats, not mine: I have rated nothing.
        var mine = await (await me.Client.GetAsync("/api/me/rating-stats"))
            .ReadAsAsync<RatingStatsDto>();
        Assert.Equal(0, mine.Count);
    }

    /// <summary>
    /// The split be-04 task 3 exists for: the friend owns the rows, the viewer owns
    /// the decoration.
    /// </summary>
    [Fact]
    public async Task A_friends_list_shows_their_rows_with_my_memberships()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        var shared = SocialWorld.Film(0);
        var theirsOnly = SocialWorld.Film(1);

        await friend.Client.RateAsync(shared, 10);
        await friend.Client.RateAsync(theirsOnly, 3);

        await me.Client.AddAndReadAsync("watchlist", shared);
        await me.Client.RateAsync(shared, 4);

        var page = await (await me.Client.GetAsync($"/api/friends/{friend.Id}/lists/watched"))
            .ReadAsAsync<ListPageDto>();

        Assert.Equal(2, page.Count);

        var sharedEntry = Assert.Single(page.Entries, e => e.Title.TmdbId == shared);
        Assert.Equal(10, sharedEntry.Rating);            // theirs, on the entry
        Assert.Equal(4, sharedEntry.Title.MyRating);      // mine, on the card
        Assert.True(sharedEntry.Title.Lists.Watchlist);   // mine
        Assert.True(sharedEntry.Title.Lists.Watched);     // mine

        var otherEntry = Assert.Single(page.Entries, e => e.Title.TmdbId == theirsOnly);
        Assert.Equal(3, otherEntry.Rating);
        Assert.Null(otherEntry.Title.MyRating);
        Assert.False(otherEntry.Title.Lists.Watched);
        Assert.False(otherEntry.Title.Lists.Watchlist);
    }

    [Fact]
    public async Task An_unknown_list_name_on_a_friends_route_is_404()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        var response = await me.Client.GetAsync($"/api/friends/{friend.Id}/lists/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
    }

    /// <summary>FR-G4 — the per-film friend context on <c>GET /api/titles/{key}</c>.</summary>
    [Fact]
    public async Task Film_detail_lists_friends_who_watched_it_and_nobody_else()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("viewer");
        var rater = await world.JoinAsync("rater");
        var unrated = await world.JoinAsync("unrated");
        var stranger = await world.JoinAsync("stranger");

        await TestApi.BefriendAsync(me.Client, me.Id, rater.Client, rater.Id);
        await TestApi.BefriendAsync(me.Client, me.Id, unrated.Client, unrated.Id);

        var film = SocialWorld.Film(0);
        await rater.Client.RateAsync(film, 8);
        await unrated.Client.AddAndReadAsync("watched", film);
        await stranger.Client.RateAsync(film, 10);

        var detail = await (await me.Client.GetAsync($"/api/titles/{TestApi.Movie(film)}"))
            .ReadAsAsync<TitleDetailDto>();

        Assert.Equal(2, detail.FriendsWatched.Length);

        // Rated first, unrated last.
        Assert.Equal(rater.Id.ToString(), detail.FriendsWatched[0].User.Id);
        Assert.Equal(8, detail.FriendsWatched[0].Rating);
        Assert.Equal(unrated.Id.ToString(), detail.FriendsWatched[1].User.Id);
        Assert.Null(detail.FriendsWatched[1].Rating);

        Assert.DoesNotContain(detail.FriendsWatched, f => f.User.Id == stranger.Id.ToString());

        // Unfriending takes them off the page immediately.
        await me.Client.UnfriendAsync(rater.Id);

        var after = await (await me.Client.GetAsync($"/api/titles/{TestApi.Movie(film)}"))
            .ReadAsAsync<TitleDetailDto>();
        Assert.Equal(unrated.Id.ToString(), Assert.Single(after.FriendsWatched).User.Id);
    }
}
