namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// Which list a suggestion is for. Not <see cref="ListKind"/>: nobody suggests
/// that you have already watched something.
/// </summary>
public enum SuggestionTarget { Watchlist = 1, Queue = 2 }

/// <summary>
/// Where a suggestion is between arriving and being answered.
///
/// The distinction that matters is <see cref="Pending"/> versus
/// <see cref="Added"/>: both are unanswered, but only <see cref="Added"/> claims
/// there is a <see cref="ListEntry"/> the suggestion itself created — which is
/// what "remove" is allowed to delete. A dismissed suggestion is not a state; the
/// row is deleted, like a declined friend request.
/// </summary>
public enum SuggestionState { Pending = 1, Added = 2, Accepted = 3 }

/// <summary>
/// One friend recommending one title to another (plan 10).
///
/// At most one row per <c>(from, to, title)</c> — the unique index says so. A
/// re-suggestion after acceptance rewrites this row rather than adding a second,
/// so a single title can never accumulate a stack of suggestions from one person.
/// </summary>
public class Suggestion
{
    /// <summary>
    /// Shorter than a watched note on purpose: a note is what you thought of
    /// something you saw, a suggestion comment is the reason you are asking.
    /// </summary>
    public const int MaxCommentLength = 500;

    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public AppUser FromUser { get; set; } = null!;
    public Guid ToUserId { get; set; }
    public AppUser ToUser { get; set; } = null!;
    public required string TitleKey { get; set; }
    public Title Title { get; set; } = null!;
    public SuggestionTarget Target { get; set; }

    /// <summary>
    /// The suggester's intended queue position, 0-based. Null on a watchlist
    /// suggestion and on a queue suggestion that just means "sometime". Read once,
    /// when the entry is created, and never re-asserted afterwards: a friend gets
    /// to make the case, not to hold a slot.
    /// </summary>
    public int? Position { get; set; }

    /// <summary>Why they are asking. At most 500 characters; see API-CONTRACT.md.</summary>
    public string? Comment { get; set; }

    public SuggestionState State { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>The two states that are still waiting on the recipient.</summary>
    public bool IsLive => State is SuggestionState.Pending or SuggestionState.Added;
}
