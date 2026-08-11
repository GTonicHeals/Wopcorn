using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Friend-to-friend suggestions (plan 10).
///
/// The rule everything here is written against: a suggestion may write to the
/// recipient's lists, but only ever to <b>add</b>, and only ever a row it created
/// itself. The <c>added</c> state exists solely to record which rows those are.
/// </summary>
public class SuggestionsTests
{
    private const string Film = "movie-1001";
    private const string Other = "movie-1002";

    // --- sending ------------------------------------------------------------

    [Fact]
    public async Task A_suggestion_waits_in_the_inbox_when_auto_add_is_off()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(
            nora.Id, Film, "queue", position: 0, comment: "Watch it before we talk.");

        Assert.Equal("pending", sent.State);
        Assert.Equal("queue", sent.Target);
        Assert.Equal(0, sent.Position);
        Assert.Equal("Watch it before we talk.", sent.Comment);

        // Off is the default, and nothing at all has been written to her lists.
        var queue = await nora.Client.GetListAsync("queue");
        Assert.Empty(queue.Entries);

        var inbox = await nora.Client.GetSuggestionsAsync();
        var incoming = Assert.Single(inbox.Incoming);
        Assert.Equal(sam.Id.ToString(), incoming.From.Id);
        Assert.Equal("pending", incoming.State);
    }

    [Fact]
    public async Task Auto_add_puts_the_title_straight_on_the_list()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "watchlist");
        Assert.Equal("added", sent.State);

        var watchlist = await nora.Client.GetListAsync("watchlist");
        var entry = Assert.Single(watchlist.Entries);
        Assert.Equal(Film, entry.Title.Key);

        // And it says who it came from, which is the whole point of the state.
        Assert.NotNull(entry.Title.Suggestion);
        Assert.Equal("added", entry.Title.Suggestion.State);
        Assert.Equal(sam.Id.ToString(), entry.Title.Suggestion.From.Id);
    }

    [Fact]
    public async Task Auto_add_never_adopts_an_entry_the_recipient_already_had()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        var mine = await nora.Client.AddAndReadAsync("watchlist", Film);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "watchlist");

        // Pending despite auto-add: "remove" on a badge attached to her own entry
        // would delete her own work, so the suggestion never claims it.
        Assert.Equal("pending", sent.State);

        var watchlist = await nora.Client.GetListAsync("watchlist");
        Assert.Equal(mine.AddedAt, Assert.Single(watchlist.Entries).AddedAt);
    }

    [Fact]
    public async Task Suggesting_to_a_stranger_is_forbidden()
    {
        using var world = await SocialWorld.CreateAsync();
        var sam = await world.JoinAsync("sam");
        var nora = await world.JoinAsync("nora");

        var response = await sam.Client.SuggestAsync(nora.Id, Film, "queue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Suggesting_to_yourself_is_a_bad_request()
    {
        using var world = await SocialWorld.CreateAsync();
        var sam = await world.JoinAsync("sam");

        var response = await sam.Client.SuggestAsync(sam.Id, Film, "queue");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Watched_is_not_a_target()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        // Nobody suggests that you have already seen something.
        var response = await sam.Client.SuggestAsync(nora.Id, Film, "watched");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadApiErrorAsync()).Errors!.ContainsKey("target"));
    }

    [Fact]
    public async Task A_second_live_suggestion_of_the_same_title_conflicts()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");
        var again = await sam.Client.SuggestAsync(nora.Id, Film, "watchlist");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("suggestion_pending", (await again.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task Two_friends_may_both_suggest_the_same_title()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        var alex = await world.JoinAsync("alex");
        await TestApi.BefriendAsync(alex.Client, alex.Id, nora.Client, nora.Id);

        await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");
        await alex.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        var inbox = await nora.Client.GetSuggestionsAsync();
        Assert.Equal(2, inbox.Incoming.Length);

        // The card has room for one attribution line, so it carries the newest; the
        // title screen carries both.
        var detail = await nora.Client.GetTitleAsync(Film);
        Assert.Equal(alex.Id.ToString(), detail.Suggestion!.From.Id);
        Assert.Equal(2, detail.SuggestedBy.Length);
    }

    [Fact]
    public async Task The_suggesters_own_rating_travels_with_the_suggestion()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        await sam.Client.RateAsync(Film, 9);
        await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue", comment: "Trust me.");

        // "My friend gave this a 9 and thinks I should watch it" is most of the
        // reason to look at a suggestion at all.
        var detail = await nora.Client.GetTitleAsync(Film);
        Assert.Equal(9, detail.Suggestion!.FromRating);
        Assert.Equal("Trust me.", detail.Suggestion.Comment);
    }

    // --- answering ----------------------------------------------------------

    [Fact]
    public async Task Accepting_a_pending_suggestion_adds_the_title_and_clears_the_badge()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "watchlist");

        var response = await nora.Client.AcceptSuggestionAsync(sent.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("accepted", (await response.ReadAsAsync<SuggestionDto>()).State);

        var watchlist = await nora.Client.GetListAsync("watchlist");
        var entry = Assert.Single(watchlist.Entries);
        Assert.Equal(Film, entry.Title.Key);

        // The badge is a call to action, and there is no longer an action to call
        // for. The title stays; "recommended by X — accept / remove" does not.
        Assert.Null(entry.Title.Suggestion);
    }

    [Fact]
    public async Task Accepting_an_auto_added_suggestion_leaves_the_title_exactly_where_it_was()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");
        var before = await world.Factory.QueuePositionsAsync(nora.Id);

        await nora.Client.AcceptSuggestionAsync(sent.Id);

        Assert.Equal(before, await world.Factory.QueuePositionsAsync(nora.Id));
        Assert.Null(Assert.Single((await nora.Client.GetListAsync("queue")).Entries).Title.Suggestion);
    }

    [Fact]
    public async Task An_accepted_suggestion_leaves_the_inbox_but_stays_on_the_title_screen()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(
            nora.Id, Film, "queue", comment: "You will like this one.");
        await nora.Client.AcceptSuggestionAsync(sent.Id);

        Assert.Empty((await nora.Client.GetSuggestionsAsync()).Incoming);

        // Who recommended a title and what they said about it is permanent; only
        // the prompt to answer goes away.
        var detail = await nora.Client.GetTitleAsync(Film);
        var note = Assert.Single(detail.SuggestedBy);
        Assert.Equal("accepted", note.State);
        Assert.Equal("You will like this one.", note.Comment);

        // The sender can see what became of it.
        var outgoing = Assert.Single((await sam.Client.GetSuggestionsAsync()).Outgoing);
        Assert.Equal("accepted", outgoing.State);
    }

    [Fact]
    public async Task Dismissing_an_auto_added_suggestion_removes_the_entry_it_created()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");
        Assert.Equal("added", sent.State);

        var response = await nora.Client.DismissSuggestionAsync(sent.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Empty((await nora.Client.GetListAsync("queue")).Entries);
    }

    [Fact]
    public async Task Dismissing_a_pending_suggestion_touches_no_list()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        await nora.Client.AddAndReadAsync("watchlist", Film);
        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "watchlist");
        Assert.Equal("pending", sent.State);

        await nora.Client.DismissSuggestionAsync(sent.Id);

        // Her own entry survives: a pending suggestion never wrote to a list, so
        // dismissing it cannot take one away.
        Assert.Single((await nora.Client.GetListAsync("watchlist")).Entries);
    }

    [Fact]
    public async Task Dismissing_leaves_no_trace_and_the_sender_may_try_again()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");
        await nora.Client.DismissSuggestionAsync(sent.Id);

        // Like declining a friend request: gone from both sides.
        Assert.Empty((await sam.Client.GetSuggestionsAsync()).Outgoing);
        Assert.Empty((await nora.Client.GetSuggestionsAsync()).Incoming);

        var again = await sam.Client.SuggestAsync(nora.Id, Film, "queue");
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Re_suggesting_after_acceptance_rewrites_the_row_rather_than_adding_one()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var first = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "watchlist");
        await nora.Client.AcceptSuggestionAsync(first.Id);

        var second = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("queue", second.Target);
        Assert.Single((await sam.Client.GetSuggestionsAsync()).Outgoing);
    }

    // --- who may act --------------------------------------------------------

    [Fact]
    public async Task The_sender_may_not_accept_their_own_suggestion()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        Assert.Equal(
            HttpStatusCode.Forbidden, (await sam.Client.AcceptSuggestionAsync(sent.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden, (await sam.Client.DismissSuggestionAsync(sent.Id)).StatusCode);
    }

    [Fact]
    public async Task The_recipient_may_not_withdraw_a_suggestion_made_to_them()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        // She has dismiss; withdrawal is the sender's verb, and the two sides are
        // not interchangeable.
        var response = await nora.Client.WithdrawSuggestionAsync(sent.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Withdrawing_takes_back_the_message_and_never_the_title()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        var response = await sam.Client.WithdrawSuggestionAsync(sent.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // By now it is a row in someone else's queue, possibly already moved. All
        // that disappears is the attribution.
        var entry = Assert.Single((await nora.Client.GetListAsync("queue")).Entries);
        Assert.Equal(Film, entry.Title.Key);
        Assert.Null(entry.Title.Suggestion);
    }

    [Fact]
    public async Task An_unknown_suggestion_is_not_found()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var id = Guid.NewGuid().ToString();

        Assert.Equal(
            HttpStatusCode.NotFound, (await nora.Client.AcceptSuggestionAsync(id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound, (await sam.Client.WithdrawSuggestionAsync(id)).StatusCode);
    }

    // --- queue position -----------------------------------------------------

    [Fact]
    public async Task A_suggested_position_inserts_and_shifts_the_rest_down()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        await nora.Client.AddAndReadAsync("queue", "movie-1003");
        await nora.Client.AddAndReadAsync("queue", "movie-1004");
        await nora.Client.AddAndReadAsync("queue", "movie-1005");

        await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue", position: 1);

        Assert.Equal(
            [("movie-1003", 0), (Film, 1), ("movie-1004", 2), ("movie-1005", 3)],
            await world.Factory.QueuePositionsAsync(nora.Id));
    }

    [Fact]
    public async Task A_position_past_the_end_is_clamped_rather_than_rejected()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");
        await nora.Client.SetAutoAddAsync(true);

        await nora.Client.AddAndReadAsync("queue", "movie-1003");

        // The queue moved under the suggester, which is ordinary; the number is
        // clamped to the end rather than failing the suggestion.
        await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue", position: 99);

        Assert.Equal(
            [("movie-1003", 0), (Film, 1)], await world.Factory.QueuePositionsAsync(nora.Id));
    }

    [Fact]
    public async Task A_negative_position_is_a_bad_request()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var response = await sam.Client.SuggestAsync(nora.Id, Film, "queue", position: -1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadApiErrorAsync()).Errors!.ContainsKey("position"));
    }

    [Fact]
    public async Task A_position_on_a_watchlist_suggestion_is_discarded()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Other, "watchlist", position: 3);

        // The watchlist has no order, so a position on one means nothing and is not
        // stored to confuse a later reader.
        Assert.Null(sent.Position);
    }

    [Fact]
    public async Task An_accepted_position_is_honoured_once_and_never_re_asserted()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        await nora.Client.AddAndReadAsync("queue", "movie-1003");
        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue", position: 0);
        await nora.Client.AcceptSuggestionAsync(sent.Id);

        Assert.Equal(
            [(Film, 0), ("movie-1003", 1)], await world.Factory.QueuePositionsAsync(nora.Id));

        // Hers now: she moves it, and nothing puts it back.
        await nora.Client.ReorderQueueAsync("movie-1003", Film);

        Assert.Equal(
            [("movie-1003", 0), (Film, 1)], await world.Factory.QueuePositionsAsync(nora.Id));
    }

    // --- the setting --------------------------------------------------------

    [Fact]
    public async Task Auto_add_is_off_until_it_is_turned_on()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        // Signing up must not hand every future friend write access to your queue.
        Assert.False((await me.Client.GetMeAsync()).AutoAddSuggestions);

        await me.Client.SetAutoAddAsync(true);
        Assert.True((await me.Client.GetMeAsync()).AutoAddSuggestions);

        await me.Client.SetAutoAddAsync(false);
        Assert.False((await me.Client.GetMeAsync()).AutoAddSuggestions);
    }

    [Fact]
    public async Task The_setting_belongs_to_the_recipient_not_the_sender()
    {
        using var world = await SocialWorld.CreateAsync();
        var (sam, nora) = await world.JoinFriendsAsync("sam", "nora");

        // Sam auto-adds; Nora does not. What matters is where the title is going.
        await sam.Client.SetAutoAddAsync(true);

        var sent = await sam.Client.SuggestAndReadAsync(nora.Id, Film, "queue");

        Assert.Equal("pending", sent.State);
        Assert.Empty((await nora.Client.GetListAsync("queue")).Entries);
    }
}
