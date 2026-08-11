using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Notes on watched titles (plan 10).
///
/// The obligation these are written against is that a comment behaves exactly
/// like a rating at every edge — implicit add, survival on clear, discard on
/// removal — because that is what makes the two one rule rather than two.
/// </summary>
public class CommentsTests
{
    private const string Key = "movie-1001";

    [Fact]
    public async Task Writing_a_note_implicitly_marks_the_title_watched()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        var entry = await me.Client.CommentAndReadAsync(Key, "Held up better than I expected.");

        Assert.Equal("Held up better than I expected.", entry.Comment);
        Assert.True(entry.Title.Lists.Watched);

        var watched = await me.Client.GetListAsync("watched");
        Assert.Equal(Key, Assert.Single(watched.Entries).Title.Key);
    }

    [Fact]
    public async Task A_note_is_trimmed_and_replaces_the_previous_one()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "First thoughts.");
        var second = await me.Client.CommentAndReadAsync(Key, "   Second thoughts.   ");

        Assert.Equal("Second thoughts.", second.Comment);

        // One note per watched title, not a thread: the second write replaced the
        // first rather than adding a row beside it.
        var watched = await me.Client.GetListAsync("watched");
        Assert.Equal("Second thoughts.", Assert.Single(watched.Entries).Comment);
    }

    [Fact]
    public async Task Clearing_a_note_keeps_the_watched_entry()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "Worth it.");

        var cleared = await me.Client.ClearCommentAsync(Key);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        // Exactly what DELETE .../rating does: the judgement goes, the fact of
        // having watched it stays.
        var watched = await me.Client.GetListAsync("watched");
        var entry = Assert.Single(watched.Entries);
        Assert.Equal(Key, entry.Title.Key);
        Assert.Null(entry.Comment);
    }

    [Fact]
    public async Task Clearing_a_note_that_is_not_there_is_still_no_content()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        var response = await me.Client.ClearCommentAsync(Key);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unwatching_a_title_discards_its_note()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "Gone in a moment.");
        await me.Client.RemoveFromListAsync("watched", Key);

        // The note lived on the watched row, so it went with it — re-watching does
        // not resurrect an opinion the user threw away.
        await me.Client.AddAndReadAsync("watched", Key);
        var watched = await me.Client.GetListAsync("watched");
        Assert.Null(Assert.Single(watched.Entries).Comment);
    }

    [Fact]
    public async Task A_blank_note_is_rejected_rather_than_silently_clearing()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "Something I meant to keep.");

        var response = await me.Client.CommentAsync(Key, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.True(error.Errors!.ContainsKey("comment"));

        // And the note it would have destroyed is still there.
        var watched = await me.Client.GetListAsync("watched");
        Assert.Equal("Something I meant to keep.", Assert.Single(watched.Entries).Comment);
    }

    [Fact]
    public async Task A_note_over_the_limit_is_rejected()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        var response = await me.Client.CommentAsync(Key, new string('x', 2001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_malformed_key_is_a_bad_request_not_a_missing_title()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        var response = await me.Client.CommentAsync("not-a-key", "Hello.");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    // --- visibility ---------------------------------------------------------

    [Fact]
    public async Task A_friend_sees_the_note_on_the_title_screen_beside_the_rating()
    {
        using var world = await SocialWorld.CreateAsync();
        var (author, reader) = await world.JoinFriendsAsync("nora", "sam");

        await author.Client.RateAsync(Key, 9);
        await author.Client.CommentAndReadAsync(Key, "The ending is the whole film.");

        var detail = await reader.Client.GetTitleAsync(Key);

        var watched = Assert.Single(detail.FriendsWatched);
        Assert.Equal(author.Id.ToString(), watched.User.Id);
        Assert.Equal(9, watched.Rating);
        Assert.Equal("The ending is the whole film.", watched.Comment);
    }

    [Fact]
    public async Task A_friend_sees_the_note_on_the_watched_list_it_belongs_to()
    {
        using var world = await SocialWorld.CreateAsync();
        var (author, reader) = await world.JoinFriendsAsync("nora", "sam");

        await author.Client.CommentAndReadAsync(Key, "A note on a list.");

        var response = await reader.Client.GetAsync($"/api/friends/{author.Id}/lists/watched");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // `comment` follows `rating`: it is the list owner's, while the card stays
        // the viewer's.
        var page = await response.ReadAsAsync<ListPageDto>();
        var entry = Assert.Single(page.Entries);
        Assert.Equal("A note on a list.", entry.Comment);
        Assert.False(entry.Title.Lists.Watched);
    }

    [Fact]
    public async Task A_stranger_sees_nothing_of_a_note()
    {
        using var world = await SocialWorld.CreateAsync();
        var author = await world.JoinAsync("nora");
        var stranger = await world.JoinAsync("sam");

        await author.Client.CommentAndReadAsync(Key, "Private until we are friends.");

        var detail = await stranger.Client.GetTitleAsync(Key);
        Assert.Empty(detail.FriendsWatched);

        var list = await stranger.Client.GetAsync($"/api/friends/{author.Id}/lists/watched");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task My_own_note_comes_back_on_the_title_detail()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "Mine to see.");

        var detail = await me.Client.GetTitleAsync(Key);

        // myComment, not friendsWatched: the feed is other people's news and
        // friendsWatched is other people's ratings. Your own note is neither.
        Assert.Equal("Mine to see.", detail.MyComment);
        Assert.Empty(detail.FriendsWatched);
    }

    [Fact]
    public async Task A_note_writes_no_activity_of_its_own()
    {
        using var world = await SocialWorld.CreateAsync();
        var me = await world.JoinAsync("nora");

        await me.Client.CommentAndReadAsync(Key, "Quietly.");

        // The implicit Watched event is there, because writing a note is watching.
        // A "commented" event is not: a note is prose addressed to whoever opens the
        // title, not an announcement.
        var activity = await world.Factory.ActivityAsync(me.Id);
        Assert.Equal(["Watched"], activity.Select(a => a.Kind.ToString()));
    }
}
