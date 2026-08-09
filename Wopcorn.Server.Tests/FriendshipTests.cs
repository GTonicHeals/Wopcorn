using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The friend handshake (FR-F1..FR-F5) — API-CONTRACT.md "Friends".
///
/// The rules worth pinning down are the ones about <b>who</b> may act: only the
/// recipient answers a request, and a request in either direction blocks a second
/// row rather than creating a mirrored pair that could both be accepted.
/// </summary>
public class FriendshipTests
{
    [Fact]
    public async Task The_sender_cannot_accept_their_own_request_but_the_recipient_can()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        var sent = await a.Client.SendFriendRequestAsync(b.Id);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        var request = await sent.ReadAsAsync<FriendRequestDto>();
        Assert.Equal(b.Id.ToString(), request.User.Id);      // outgoing shows the recipient

        var bySender = await a.Client.AcceptFriendRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.Forbidden, bySender.StatusCode);
        Assert.Equal("forbidden", (await bySender.ReadApiErrorAsync()).Code);

        // Nothing happened: still a pending request, still no friendship.
        Assert.Equal(1, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.Friendships.CountAsync()));

        var byRecipient = await b.Client.AcceptFriendRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.OK, byRecipient.StatusCode);

        var friend = await byRecipient.ReadAsAsync<FriendDto>();
        Assert.Equal(a.Id.ToString(), friend.User.Id);
        Assert.NotNull(friend.TasteMatch);

        // FR-F5: the request is consumed and exactly one friendship row exists.
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
        Assert.Equal(1, await world.Factory.QueryAsync(db => db.Friendships.CountAsync()));
    }

    [Fact]
    public async Task Friendship_is_stored_as_one_normalised_row()
    {
        using var world = await SocialWorld.CreateAsync();
        var (a, b) = await world.JoinFriendsAsync("ada", "bo");

        var rows = await world.Factory.QueryAsync(db => db.Friendships.AsNoTracking().ToListAsync());

        var row = Assert.Single(rows);
        Assert.True(row.UserAId.CompareTo(row.UserBId) < 0);
        Assert.Equal(
            new[] { a.Id, b.Id }.OrderBy(id => id),
            new[] { row.UserAId, row.UserBId }.OrderBy(id => id));
    }

    [Fact]
    public async Task A_reverse_request_while_one_is_pending_is_409_and_creates_no_second_row()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        Assert.Equal(HttpStatusCode.Created, (await a.Client.SendFriendRequestAsync(b.Id)).StatusCode);

        var reverse = await b.Client.SendFriendRequestAsync(a.Id);
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);
        Assert.Equal("request_pending", (await reverse.ReadApiErrorAsync()).Code);

        // Same direction again is also request_pending.
        var repeat = await a.Client.SendFriendRequestAsync(b.Id);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal("request_pending", (await repeat.ReadApiErrorAsync()).Code);

        Assert.Equal(1, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
    }

    [Fact]
    public async Task Requesting_an_existing_friend_is_409_already_friends()
    {
        using var world = await SocialWorld.CreateAsync();
        var (a, b) = await world.JoinFriendsAsync("ada", "bo");

        var again = await a.Client.SendFriendRequestAsync(b.Id);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_friends", (await again.ReadApiErrorAsync()).Code);
    }

    /// <summary>The plan's task 1 verification, start to finish.</summary>
    [Fact]
    public async Task Remove_lets_the_pair_start_over()
    {
        using var world = await SocialWorld.CreateAsync();
        var (a, b) = await world.JoinFriendsAsync("ada", "bo");

        // Either party may remove (FR-F3) — here the one who did not send.
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.UnfriendAsync(a.Id)).StatusCode);
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.Friendships.CountAsync()));

        // Idempotent: removing again is still 204.
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.UnfriendAsync(a.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await a.Client.SendFriendRequestAsync(b.Id)).StatusCode);
    }

    [Fact]
    public async Task Declining_removes_the_request_and_only_the_recipient_may_do_it()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        var request = await (await a.Client.SendFriendRequestAsync(b.Id))
            .ReadAsAsync<FriendRequestDto>();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await a.Client.DeclineFriendRequestAsync(request.Id)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await b.Client.DeclineFriendRequestAsync(request.Id)).StatusCode);

        Assert.Equal(0, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.Friendships.CountAsync()));

        // Answering it twice is a 404, not a second acceptance.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await b.Client.AcceptFriendRequestAsync(request.Id)).StatusCode);
    }

    [Fact]
    public async Task You_cannot_befriend_yourself_or_a_stranger_who_does_not_exist()
    {
        using var world = await SocialWorld.CreateAsync();
        var a = await world.JoinAsync("ada");

        var self = await a.Client.SendFriendRequestAsync(a.Id);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("validation_failed", (await self.ReadApiErrorAsync()).Code);

        var ghost = await a.Client.SendFriendRequestAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
        Assert.Equal("not_found", (await ghost.ReadApiErrorAsync()).Code);

        Assert.Equal(0, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
    }

    /// <summary>The plan's task 2 verification.</summary>
    [Fact]
    public async Task Friends_index_separates_pending_requests_from_friends()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        await a.Client.SendFriendRequestAsync(b.Id);

        var recipient = await b.Client.GetFriendsAsync();
        Assert.Empty(recipient.Friends);
        Assert.Empty(recipient.Outgoing);
        Assert.Equal(a.Id.ToString(), Assert.Single(recipient.Incoming).User.Id);

        var sender = await a.Client.GetFriendsAsync();
        Assert.Empty(sender.Friends);
        Assert.Empty(sender.Incoming);
        Assert.Equal(b.Id.ToString(), Assert.Single(sender.Outgoing).User.Id);

        await b.Client.AcceptFriendRequestAsync(recipient.Incoming[0].Id);

        var after = await a.Client.GetFriendsAsync();
        Assert.Empty(after.Incoming);
        Assert.Empty(after.Outgoing);

        var friend = Assert.Single(after.Friends);
        Assert.Equal(b.Id.ToString(), friend.User.Id);
        Assert.Equal("bo", friend.User.DisplayName);
        Assert.NotEqual(default, friend.FriendsSince);
        Assert.False(friend.TasteMatch.Qualified);
        Assert.Equal(0, friend.TasteMatch.SharedCount);
        Assert.Null(friend.TasteMatch.Score);
    }

    /// <summary>
    /// be-04 "Done when": <c>GET /api/friends</c> with N friends issues a constant
    /// number of queries. Measured, not asserted by inspection — the taste match is
    /// the part that would quietly become N+1.
    /// </summary>
    [Fact]
    public async Task Friends_index_costs_the_same_number_of_queries_for_one_friend_as_for_eight()
    {
        var sql = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var world = await SocialWorld.CreateAsync(sql);

        var me = await world.JoinAsync("viewer");
        var first = await world.JoinAsync("friend00");
        await TestApi.BefriendAsync(me.Client, me.Id, first.Client, first.Id);
        await me.Client.RateAsync(SocialWorld.Film(0), 7);
        await first.Client.RateAsync(SocialWorld.Film(0), 8);

        sql.Clear();
        Assert.Single((await me.Client.GetFriendsAsync()).Friends);
        var withOne = world.Factory.SqlCommandCount;

        for (var i = 1; i < 8; i++)
        {
            var friend = await world.JoinAsync($"friend{i:D2}");
            await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);
            await friend.Client.RateAsync(SocialWorld.Film(0), 6);
        }

        // A rating write invalidates the actor's cached matches, so this call
        // recomputes rather than reading eight cache hits.
        await me.Client.RateAsync(SocialWorld.Film(1), 5);

        sql.Clear();
        Assert.Equal(8, (await me.Client.GetFriendsAsync()).Friends.Length);

        Assert.Equal(withOne, world.Factory.SqlCommandCount);
    }

    [Fact]
    public async Task User_search_is_a_case_insensitive_prefix_that_excludes_you_and_reports_the_relationship()
    {
        using var world = await SocialWorld.CreateAsync();

        var me = await world.JoinAsync("Amelia");
        var friend = await world.JoinAsync("Amos");
        var invited = await world.JoinAsync("Ambrose");
        var inviter = await world.JoinAsync("Amara");
        await world.JoinAsync("Zara");

        await TestApi.BefriendAsync(me.Client, me.Id, friend.Client, friend.Id);
        await me.Client.SendFriendRequestAsync(invited.Id);
        await inviter.Client.SendFriendRequestAsync(me.Id);

        var results = await (await me.Client.GetAsync("/api/users/search?q=am"))
            .ReadAsAsync<UserSearchResultDto[]>();

        Assert.DoesNotContain(results, r => r.Id == me.Id.ToString());
        Assert.DoesNotContain(results, r => r.DisplayName == "Zara");

        var byId = results.ToDictionary(r => r.Id, r => r.Relationship);
        Assert.Equal("friends", byId[friend.Id.ToString()]);
        Assert.Equal("request_sent", byId[invited.Id.ToString()]);
        Assert.Equal("request_received", byId[inviter.Id.ToString()]);

        // Blank q is an empty list, not a 400 and not everybody.
        Assert.Empty(await (await me.Client.GetAsync("/api/users/search?q=")).
            ReadAsAsync<UserSearchResultDto[]>());

        // Prefix, not substring: "mel" is inside "Amelia" but does not start it.
        Assert.Empty(await (await me.Client.GetAsync("/api/users/search?q=mel")).
            ReadAsAsync<UserSearchResultDto[]>());
    }

    [Fact]
    public async Task Only_the_sender_can_withdraw_a_request_and_the_recipient_gets_403()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        var request = await (await a.Client.SendFriendRequestAsync(b.Id))
            .ReadAsAsync<FriendRequestDto>();

        // The recipient has decline, not withdraw. The two verbs are not
        // interchangeable, or either side could act for the other.
        var byRecipient = await b.Client.CancelFriendRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.Forbidden, byRecipient.StatusCode);
        Assert.Equal("forbidden", (await byRecipient.ReadApiErrorAsync()).Code);
        Assert.Equal(1, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));

        var bySender = await a.Client.CancelFriendRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.NoContent, bySender.StatusCode);

        // No trace: no request, and no friendship conjured by the deletion.
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
        Assert.Equal(0, await world.Factory.QueryAsync(db => db.Friendships.CountAsync()));

        // Withdrawing twice is 404, not 500 — the row is genuinely gone.
        var again = await a.Client.CancelFriendRequestAsync(request.Id);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("not_found", (await again.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_withdrawn_request_leaves_both_sides_free_to_send_again()
    {
        using var world = await SocialWorld.CreateAsync();

        var a = await world.JoinAsync("ada");
        var b = await world.JoinAsync("bo");

        var request = await (await a.Client.SendFriendRequestAsync(b.Id))
            .ReadAsAsync<FriendRequestDto>();

        // While it stands, the reverse direction is still blocked (FR-F1).
        Assert.Equal(HttpStatusCode.Conflict, (await b.Client.SendFriendRequestAsync(a.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await a.Client.CancelFriendRequestAsync(request.Id)).StatusCode);

        // Withdrawing clears the block in both directions, and the pair read as
        // strangers again rather than as a lingering "request_sent".
        var seenByA = await (await a.Client.GetAsync("/api/users/search?q=bo"))
            .ReadAsAsync<UserSearchResultDto[]>();
        Assert.Equal("none", Assert.Single(seenByA, r => r.Id == b.Id.ToString()).Relationship);

        Assert.Equal(HttpStatusCode.Created, (await b.Client.SendFriendRequestAsync(a.Id)).StatusCode);
        Assert.Equal(1, await world.Factory.QueryAsync(db => db.FriendRequests.CountAsync()));
    }
}
