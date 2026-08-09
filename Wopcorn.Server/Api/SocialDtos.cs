using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

// API-CONTRACT.md "Friends" and "Feed". Declared here rather than beside the
// entities so the wire shape is one file the frontend track can read.

/// <summary>
/// FR-G5/FR-G6. <paramref name="Score"/> is <c>null</c> only when the pair share
/// no rated titles at all; <paramref name="Qualified"/> is <c>false</c> below
/// <see cref="Social.TasteMatchService.MinimumOverlap"/> and obliges the client to
/// hide or qualify the number. The two always travel together — a score without
/// its <paramref name="SharedCount"/> is misinformation.
/// </summary>
public record TasteMatch(int? Score, int SharedCount, bool Qualified);

public record FriendDto(UserSummary User, DateTimeOffset FriendsSince, TasteMatch TasteMatch);

/// <summary>
/// A pending request. <paramref name="User"/> is always <b>the other party</b>:
/// the sender on an incoming request, the recipient on an outgoing one.
/// </summary>
public record FriendRequestDto(string Id, UserSummary User, DateTimeOffset SentAt);

/// <summary>
/// The whole friends screen in one response, so the pending-request badge (FR-F4)
/// costs no extra round trip.
/// </summary>
public record FriendsResponse(
    IReadOnlyList<FriendDto> Friends,
    IReadOnlyList<FriendRequestDto> Incoming,
    IReadOnlyList<FriendRequestDto> Outgoing);

/// <summary>
/// FR-F1. A non-friend may be seen only as a display name and an avatar (FR-F6);
/// <paramref name="Relationship"/> exists so the row can offer the right button.
/// </summary>
public record UserSearchResult(string Id, string DisplayName, string? AvatarUrl, string Relationship)
{
    public const string None = "none";
    public const string Friends = "friends";
    public const string RequestSent = "request_sent";
    public const string RequestReceived = "request_received";
}

public record ListCounts(int Watched, int Watchlist, int Queue);

/// <summary>How many watched titles carry a given genre. Most-watched first.</summary>
public record GenreAffinity(int Id, string Name, int Count);

/// <summary>
/// The watched list's runtime, split into what is known and what is not.
///
/// A series with an empty <c>episode_run_time</c> has no runtime at all, which is
/// ordinary rather than exceptional — so the sum would silently understate. The
/// split is what lets the client say "at least 214h" instead of passing an
/// understatement off as a total.
/// </summary>
public record RuntimeOnRecord(int Minutes, int KnownTitles, int UnknownTitles);

/// <summary>
/// The whole profile screen, for whoever is being looked at (FR-G1).
///
/// Your own profile and a friend's are the same page and therefore the same DTO.
/// <paramref name="IsSelf"/> and a null <paramref name="TasteMatch"/> are the only
/// differences: there is nobody to compare you against.
/// </summary>
/// <param name="RecentActivity">
/// The <b>owner's own</b> events — precisely what <c>GET /api/feed</c> leaves out.
/// Both read the same rows, so an undone action disappears from both (FR-G7).
/// </param>
public record ProfileDto(
    UserSummary User,
    bool IsSelf,
    DateTimeOffset MemberSince,
    RatingStats Stats,
    ListCounts Counts,
    IReadOnlyList<TitleCard> Favorites,
    IReadOnlyList<GenreAffinity> TopGenres,
    RuntimeOnRecord Runtime,
    int FriendCount,
    IReadOnlyList<ActivityItem> RecentActivity,
    TasteMatch? TasteMatch);

/// <summary>
/// One row of the feed (FR-G2). <paramref name="Rating"/> is set only when
/// <paramref name="Kind"/> is <c>rated</c>.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is unchanged across media types: the copy that reads
/// "watched" works for a film, a series and a season alike.
/// </remarks>
public record ActivityItem(
    string Id,
    UserSummary User,
    string Kind,
    TitleCard Title,
    int? Rating,
    DateTimeOffset OccurredAt);

/// <summary>
/// A keyset page (FR-G3). <paramref name="NextCursor"/> is <c>null</c> exactly
/// when there is nothing older to fetch.
/// </summary>
public record FeedPage(IReadOnlyList<ActivityItem> Items, string? NextCursor);

public static class ActivityKinds
{
    /// <summary>The contract's wire strings for <see cref="ActivityKind"/>.</summary>
    public static string ToWire(this ActivityKind kind) => kind switch
    {
        ActivityKind.Rated => "rated",
        ActivityKind.Watched => "watched",
        ActivityKind.AddedWatchlist => "added_watchlist",
        ActivityKind.AddedQueue => "added_queue",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
