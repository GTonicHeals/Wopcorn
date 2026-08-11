using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

// API-CONTRACT.md "Suggestions" (plan 10). Three shapes for one row, because a
// suggestion is read in three places that need different amounts of it.

/// <summary>
/// The "recommended by X" badge, carried on <see cref="TitleCard"/> and meaning
/// exactly one thing: <b>this is unanswered</b>.
///
/// It is present only while the suggestion is <c>pending</c> or <c>added</c>, so
/// accepting one makes the accept/remove line disappear while the title stays on
/// the list — which is the whole behaviour the badge exists to produce. The
/// lasting record is <see cref="SuggestionNote"/>.
/// </summary>
public record SuggestionBadge(
    string Id,
    UserSummary From,
    string? Comment,
    int? FromRating,
    string Target,
    string State);

/// <summary>
/// A suggestion as the title screen shows it, which unlike
/// <see cref="SuggestionBadge"/> <b>survives acceptance</b>. The badge is a call
/// to action and belongs only while there is an action to call for; who
/// recommended a title and what they said about it is a permanent part of what
/// the title screen has to say.
/// </summary>
public record SuggestionNote(
    string Id,
    UserSummary From,
    string? Comment,
    int? FromRating,
    string Target,
    int? Position,
    string State,
    DateTimeOffset SentAt);

/// <summary>
/// The whole row, for <c>GET /api/suggestions</c> and every write's response.
///
/// Both parties are named, unlike <see cref="FriendRequestDto"/>, which carries
/// only "the other one". A friend request is symmetric — the same offer whichever
/// end you hold it by — but a suggestion has an author, and "recommended by X" is
/// the point of it rather than a detail of it.
/// </summary>
public record SuggestionDto(
    string Id,
    UserSummary From,
    UserSummary To,
    TitleCard Title,
    string Target,
    int? Position,
    string? Comment,
    int? FromRating,
    string State,
    DateTimeOffset SentAt);

/// <summary>
/// Both directions in one response, so the inbox badge costs no extra round trip
/// — the same reason <see cref="FriendsResponse"/> is shaped this way.
/// </summary>
public record SuggestionsResponse(
    IReadOnlyList<SuggestionDto> Incoming,
    IReadOnlyList<SuggestionDto> Outgoing);

/// <summary>The contract's wire strings for the suggestion enums.</summary>
public static class SuggestionWire
{
    public const string Watchlist = "watchlist";
    public const string Queue = "queue";

    public static string ToWire(this SuggestionTarget target) => target switch
    {
        SuggestionTarget.Watchlist => Watchlist,
        SuggestionTarget.Queue => Queue,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    public static string ToWire(this SuggestionState state) => state switch
    {
        SuggestionState.Pending => "pending",
        SuggestionState.Added => "added",
        SuggestionState.Accepted => "accepted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>
    /// The <c>target</c> field of <c>POST /api/suggestions</c>. Unlike the
    /// <c>{list}</c> path segment there is no <c>watched</c>: nobody suggests that
    /// you have already seen something.
    /// </summary>
    public static bool TryParseTarget(string? value, out SuggestionTarget target)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Watchlist:
                target = SuggestionTarget.Watchlist;
                return true;
            case Queue:
                target = SuggestionTarget.Queue;
                return true;
            default:
                target = default;
                return false;
        }
    }

    /// <summary>The list a suggestion is for, as the list layer names it.</summary>
    public static ListKind ToListKind(this SuggestionTarget target) =>
        target == SuggestionTarget.Queue ? ListKind.Queue : ListKind.Watchlist;
}
