namespace Wopcorn.Server.Api;

// API-CONTRACT.md "Lists", "Queue ordering" and "Ratings". The contract calls the
// first type `ListEntry`; it is `ListEntryDto` here because
// Wopcorn.Server.Data.Entities.ListEntry is the row it is projected from and both
// namespaces are in scope wherever the projection happens. The wire shape is
// unchanged.

/// <summary>
/// One row of a list view. <c>position</c> is non-null only on the queue;
/// <c>watchedOn</c> only on watched (OD-1).
///
/// <paramref name="Rating"/> is the rating of the person whose list this is. On
/// your own lists it repeats <c>title.myRating</c>; on
/// <c>GET /api/friends/{userId}/lists/{list}</c> it is the <b>friend's</b> rating,
/// while <c>title.myRating</c> and <c>title.lists</c> stay yours (be-04 task 3).
/// </summary>
public record ListEntryDto(
    TitleCard Title,
    DateTimeOffset AddedAt,
    int? Position,
    string? WatchedOn,
    int? Rating);

/// <summary>
/// The <c>GET /api/lists/{list}</c> body. <c>Count</c> is the <b>unfiltered</b>
/// total for the list so the view can show "42 titles" beside a filtered result
/// (FR-C4) — the media-type filter does not move it either.
/// </summary>
public record ListPage(int Count, IReadOnlyList<ListEntryDto> Entries);

/// <summary>The authoritative queue order, echoed after every write (FR-D5).</summary>
public record QueueOrder(IReadOnlyList<string> Keys);

/// <summary>
/// The <c>409 queue_out_of_sync</c> body: the contract's error shape plus the
/// stored order, so a client whose optimistic reorder lost a race can reconcile
/// rather than retry blind (FR-D5).
/// </summary>
public record QueueOutOfSync(string Code, string Message, IReadOnlyList<string> Keys);

/// <summary>
/// FR-E6. <paramref name="Distribution"/> always has ten entries; index 0 is one
/// half-star. <paramref name="Average"/> is in half-star units.
/// </summary>
public record RatingStats(int Count, double? Average, IReadOnlyList<int> Distribution)
{
    /// <summary>Ratings are integers 1–10 on the wire and in the database (FR-E2).</summary>
    public const int Buckets = 10;

    /// <summary>
    /// Pure arithmetic over grouped <c>(rating, count)</c> rows, so the
    /// <c>count == 0</c> case is unit-testable without a database — and answers
    /// <c>null</c> instead of dividing by zero.
    /// </summary>
    public static RatingStats From(IEnumerable<(int Rating, int Count)> rows)
    {
        var distribution = new int[Buckets];
        var count = 0;
        var total = 0L;

        foreach (var (rating, rowCount) in rows)
        {
            // Defensive: a rating outside 1..10 cannot be stored through the API,
            // but it must never index outside the array if one ever appears.
            if (rating is < 1 or > Buckets || rowCount <= 0)
            {
                continue;
            }

            distribution[rating - 1] += rowCount;
            count += rowCount;
            total += (long)rating * rowCount;
        }

        return count == 0
            ? new RatingStats(0, null, distribution)
            : new RatingStats(
                count,
                Math.Round((double)total / count, 2, MidpointRounding.AwayFromZero),
                distribution);
    }
}
