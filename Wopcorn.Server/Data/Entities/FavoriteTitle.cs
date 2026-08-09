namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// One slot of a user's favourites showcase — the curated row at the top of their
/// profile.
///
/// Deliberately <b>not</b> a fourth <see cref="ListKind"/>. The three lists are a
/// closed set with their own semantics (membership, queue positions, watched
/// dates, ratings), and every screen, filter and count in the app is written
/// against exactly three. A favourite carries none of that: it is an ordered
/// reference and nothing else, so it gets its own small table instead of widening
/// an enum that a dozen switch statements read.
///
/// <see cref="Position"/> is 0-based and contiguous, rewritten wholesale on every
/// <c>PUT /api/me/favorites</c>. Nothing appends to this table, so there is no
/// compaction path to get wrong.
/// </summary>
public class FavoriteTitle
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public required string TitleKey { get; set; }
    public Title Title { get; set; } = null!;

    /// <summary>0-based. Position 0 is the title the profile takes its backdrop from.</summary>
    public int Position { get; set; }
}
