using Wopcorn.Server.Catalog;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The staleness policy is the one piece of be-02 that is pure arithmetic, so it
/// gets a plain unit test: one second either side of each TTL.
/// </summary>
public class TitleCacheTtlTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Fact]
    public void Summary_TTL_is_fourteen_days()
    {
        Assert.Equal(TimeSpan.FromDays(14), TitleCacheService.SummaryTtl);
    }

    [Fact]
    public void Detail_TTL_is_seven_days()
    {
        Assert.Equal(TimeSpan.FromDays(7), TitleCacheService.DetailTtl);
    }

    [Fact]
    public void A_summary_one_second_inside_the_TTL_is_fresh()
    {
        var fetchedAt = Now - TitleCacheService.SummaryTtl + OneSecond;

        Assert.True(TitleCacheService.IsSummaryFresh(fetchedAt, Now));
    }

    [Fact]
    public void A_summary_one_second_past_the_TTL_is_stale()
    {
        var fetchedAt = Now - TitleCacheService.SummaryTtl - OneSecond;

        Assert.False(TitleCacheService.IsSummaryFresh(fetchedAt, Now));
    }

    [Fact]
    public void A_detail_one_second_inside_the_TTL_is_fresh()
    {
        var fetchedAt = Now - TitleCacheService.DetailTtl + OneSecond;

        Assert.True(TitleCacheService.IsDetailFresh(fetchedAt, Now));
    }

    [Fact]
    public void A_detail_one_second_past_the_TTL_is_stale()
    {
        var fetchedAt = Now - TitleCacheService.DetailTtl - OneSecond;

        Assert.False(TitleCacheService.IsDetailFresh(fetchedAt, Now));
    }

    [Fact]
    public void A_film_that_has_never_had_a_detail_fetch_is_stale()
    {
        Assert.False(TitleCacheService.IsDetailFresh(null, Now));
    }
}
