using Wopcorn.Server.Social;

namespace Wopcorn.Server.Tests;

/// <summary>
/// FR-G5/FR-G6. The formula is fixed in be-04 task 4:
///
/// <code>
/// MAD   = (1/n) * Σ |ratingA - ratingB|
/// score = round(100 * (1 - MAD / 9))
/// </code>
///
/// The arithmetic is checked as a plain unit test — <see cref="TasteMatchService.Compute"/>
/// touches no database — and the wiring is checked once through HTTP.
/// </summary>
public class TasteMatchTests
{
    [Fact]
    public void Zero_overlap_is_a_null_score()
    {
        var match = TasteMatchService.Compute(0, 0);

        Assert.Null(match.Score);
        Assert.Equal(0, match.SharedCount);
        Assert.False(match.Qualified);
    }

    [Fact]
    public void Identical_ratings_are_one_hundred_and_opposite_ratings_are_zero()
    {
        Assert.Equal(100, TasteMatchService.Compute(6, 0).Score);

        // Six films, every one a full 9 apart: MAD 9, score 0.
        Assert.Equal(0, TasteMatchService.Compute(6, 6 * 9).Score);
    }

    /// <summary>
    /// Hand-computed: differences 1, 0, 2, 1, 3, 1 over six shared films.
    /// Σ = 8, MAD = 8/6 = 1.333…, 1 - 1.333/9 = 0.85185…, ×100 = 85.185… → 85.
    /// </summary>
    [Fact]
    public void A_hand_computed_set_matches()
    {
        var match = TasteMatchService.Compute(6, 1 + 0 + 2 + 1 + 3 + 1);

        Assert.Equal(85, match.Score);
        Assert.Equal(6, match.SharedCount);
        Assert.True(match.Qualified);
    }

    [Fact]
    public void Rounding_is_away_from_zero_at_the_half()
    {
        // Two films, Σ = 3: MAD 1.5, 100 * (1 - 1.5/9) = 83.33… → 83.
        Assert.Equal(83, TasteMatchService.Compute(2, 3).Score);

        // Nine films, Σ = 27: MAD 3, 100 * (1 - 3/9) = 66.66… → 67.
        Assert.Equal(67, TasteMatchService.Compute(9, 27).Score);

        // Two films, Σ = 9: MAD 4.5, exactly half the maximum gap → 50, no rounding.
        Assert.Equal(50, TasteMatchService.Compute(2, 9).Score);

        // Eight films, Σ = 9: MAD 1.125, 100 * (1 - 0.125) = 87.5 exactly — the
        // midpoint, which must round away from zero to 88 rather than to even 88.
        Assert.Equal(88, TasteMatchService.Compute(8, 9).Score);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void Qualified_turns_on_at_the_minimum_overlap(int sharedCount, bool qualified)
    {
        Assert.Equal(5, TasteMatchService.MinimumOverlap);

        // A perfect match below the threshold is still unqualified: the score is
        // computed and returned, but the client must not headline it (FR-G6).
        var match = TasteMatchService.Compute(sharedCount, 0);

        Assert.Equal(100, match.Score);
        Assert.Equal(qualified, match.Qualified);
        Assert.Equal(sharedCount, match.SharedCount);
    }

    /// <summary>The plan's task 4 verification, end to end over HTTP.</summary>
    [Fact]
    public async Task Three_shared_ratings_do_not_qualify_and_six_do()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        // Differences 1, 0, 2 over three films.
        await RateBothAsync(me, friend, 0, 8, 7);
        await RateBothAsync(me, friend, 1, 6, 6);
        await RateBothAsync(me, friend, 2, 9, 7);

        var below = await ReadMatchAsync(me, friend);
        Assert.Equal(3, below.SharedCount);
        Assert.False(below.Qualified);
        Assert.NotNull(below.Score);

        // Three more: differences 1, 3, 1 — Σ = 8 over six films (the hand
        // calculation above).
        await RateBothAsync(me, friend, 3, 5, 4);
        await RateBothAsync(me, friend, 4, 10, 7);
        await RateBothAsync(me, friend, 5, 2, 3);

        var above = await ReadMatchAsync(me, friend);
        Assert.Equal(6, above.SharedCount);
        Assert.True(above.Qualified);
        Assert.Equal(85, above.Score);
    }

    [Fact]
    public async Task Films_only_one_of_them_rated_do_not_count_as_shared()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        await RateBothAsync(me, friend, 0, 7, 7);

        // Watched by both but rated by neither, then rated by one side only.
        await me.Client.AddAndReadAsync("watched", SocialWorld.Film(1));
        await friend.Client.AddAndReadAsync("watched", SocialWorld.Film(1));
        await me.Client.RateAsync(SocialWorld.Film(2), 9);

        var match = await ReadMatchAsync(me, friend);

        Assert.Equal(1, match.SharedCount);
        Assert.Equal(100, match.Score);
        Assert.False(match.Qualified);
    }

    /// <summary>
    /// The cache is invalidated on the actor's own rating writes (task 4), so their
    /// view of the pair is never stale.
    /// </summary>
    [Fact]
    public async Task A_new_rating_by_the_viewer_updates_their_own_taste_match_immediately()
    {
        using var world = await SocialWorld.CreateAsync();
        var (me, friend) = await world.JoinFriendsAsync("viewer", "friend");

        await RateBothAsync(me, friend, 0, 8, 8);
        Assert.Equal(1, (await ReadMatchAsync(me, friend)).SharedCount);   // now cached

        await RateBothAsync(me, friend, 1, 6, 6);
        Assert.Equal(2, (await ReadMatchAsync(me, friend)).SharedCount);

        // Clearing a rating drops the film out of the overlap again.
        await me.Client.ClearRatingAsync(SocialWorld.Film(1));
        Assert.Equal(1, (await ReadMatchAsync(me, friend)).SharedCount);
    }

    private static async Task RateBothAsync(Member me, Member friend, int index, int mine, int theirs)
    {
        await me.Client.RateAsync(SocialWorld.Film(index), mine);
        await friend.Client.RateAsync(SocialWorld.Film(index), theirs);
    }

    private static async Task<TasteMatchDto> ReadMatchAsync(Member me, Member friend)
    {
        var friends = await me.Client.GetFriendsAsync();
        var row = Assert.Single(friends.Friends, f => f.User.Id == friend.Id.ToString());

        // The profile must agree with the friends list — a score is never presented
        // without its sharedCount, and never differs between the two surfaces.
        var profile = await (await me.Client.GetAsync($"/api/friends/{friend.Id}/profile"))
            .ReadAsAsync<FriendProfileDto>();

        Assert.Equal(row.TasteMatch, profile.TasteMatch);
        return row.TasteMatch;
    }
}
