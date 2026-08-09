namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// One row per (user, title, list). The three lists are independent — and so are
/// a series and its seasons, which are separate titles and therefore separate
/// entries in both directions.
/// </summary>
public class ListEntry
{
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
}
