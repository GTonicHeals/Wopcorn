namespace Wopcorn.Server.Data.Entities;

public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public AppUser FromUser { get; set; } = null!;
    public Guid ToUserId { get; set; }
    public AppUser ToUser { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
}
