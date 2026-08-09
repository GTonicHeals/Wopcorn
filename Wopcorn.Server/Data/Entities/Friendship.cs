namespace Wopcorn.Server.Data.Entities;

/// <summary>ONE row per pair, not two. Ordered so UserAId &lt; UserBId.</summary>
public class Friendship
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public AppUser UserA { get; set; } = null!;
    public Guid UserBId { get; set; }
    public AppUser UserB { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
