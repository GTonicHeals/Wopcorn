namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// One row per (user, title, list). The three lists are independent — and so are
/// a series and its seasons, which are separate titles and therefore separate
/// entries in both directions.
/// </summary>
public class ListEntry
{
    /// <summary>Long enough for a paragraph about a film, short enough not to be a blog.</summary>
    public const int MaxCommentLength = 2000;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public required string TitleKey { get; set; }
    public Title Title { get; set; } = null!;
    public ListKind Kind { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public int? Position { get; set; }               // Queue only, 0-based contiguous
    public int? Rating { get; set; }                 // Watched only, 1..10 half-stars
    public DateOnly? WatchedOn { get; set; }         // Watched only, OD-1

    /// <summary>
    /// The owner's note on having watched it (plan 10). Watched only, at most 2000
    /// characters, and visible to their friends.
    ///
    /// It lives on this row rather than in a table of its own for the same reason
    /// <see cref="Rating"/> does: there is exactly one per watched title, and
    /// removing the title from Watched has to discard it. A separate table would
    /// need a cascade to say what a nullable column already says.
    /// </summary>
    public string? Comment { get; set; }
}
