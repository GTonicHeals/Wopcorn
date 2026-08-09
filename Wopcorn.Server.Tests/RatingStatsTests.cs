using Wopcorn.Server.Api;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The one piece of be-03 that is pure arithmetic (FR-E6), so it gets a plain
/// unit test rather than an HTTP round trip.
/// </summary>
public class RatingStatsTests
{
    [Fact]
    public void An_empty_set_has_a_null_average_rather_than_a_division_by_zero()
    {
        var stats = RatingStats.From([]);

        Assert.Equal(0, stats.Count);
        Assert.Null(stats.Average);
        Assert.Equal(new int[10], stats.Distribution);
    }

    [Fact]
    public void Distribution_is_ten_buckets_indexed_from_rating_one()
    {
        var stats = RatingStats.From([(1, 2), (10, 3)]);

        Assert.Equal(10, stats.Distribution.Count);
        Assert.Equal(2, stats.Distribution[0]);
        Assert.Equal(3, stats.Distribution[9]);
        Assert.Equal(5, stats.Count);
    }

    [Fact]
    public void The_average_is_the_weighted_mean_in_half_star_units()
    {
        // 9, 9, 6 → 24 / 3
        var stats = RatingStats.From([(9, 2), (6, 1)]);

        Assert.Equal(3, stats.Count);
        Assert.Equal(8.0, stats.Average!.Value, 2);
    }

    [Fact]
    public void The_average_is_rounded_to_two_decimals()
    {
        // 7, 8, 8 → 23 / 3 = 7.666…
        var stats = RatingStats.From([(7, 1), (8, 2)]);

        Assert.Equal(7.67, stats.Average!.Value, 2);
    }

    [Fact]
    public void A_hand_computed_set_matches_bucket_for_bucket()
    {
        var stats = RatingStats.From([(2, 1), (5, 3), (8, 4), (10, 2)]);

        Assert.Equal(10, stats.Count);
        Assert.Equal([0, 1, 0, 0, 3, 0, 0, 4, 0, 2], stats.Distribution);
        // (2 + 15 + 32 + 20) / 10
        Assert.Equal(6.9, stats.Average!.Value, 2);
    }

    [Fact]
    public void A_single_rating_averages_to_itself()
    {
        var stats = RatingStats.From([(9, 1)]);

        Assert.Equal(1, stats.Count);
        Assert.Equal(9.0, stats.Average!.Value, 2);
        Assert.Equal(1, stats.Distribution[8]);
    }

    [Fact]
    public void Ratings_off_the_scale_are_dropped_rather_than_indexing_off_the_array()
    {
        // Nothing can store these through the API; the guard is there so a stray
        // row can never turn a stats read into an IndexOutOfRangeException.
        var stats = RatingStats.From([(0, 5), (11, 5), (-3, 2), (7, 1)]);

        Assert.Equal(1, stats.Count);
        Assert.Equal(7.0, stats.Average!.Value, 2);
        Assert.Equal(1, stats.Distribution.Sum());
    }
}
