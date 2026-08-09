namespace Wopcorn.Server.Data.Entities;

public enum ActivityKind { Rated = 1, Watched = 2, AddedWatchlist = 3, AddedQueue = 4 }

/// <summary>be-04 reads it; be-03 writes it.</summary>
public class ActivityEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public required string TitleKey { get; set; }
    public Title Title { get; set; } = null!;
    public ActivityKind Kind { get; set; }
    public int? Rating { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
