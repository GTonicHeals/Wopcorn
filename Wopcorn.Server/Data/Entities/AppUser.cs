using Microsoft.AspNetCore.Identity;

namespace Wopcorn.Server.Data.Entities;

public class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string? AvatarPath { get; set; }          // relative path under wwwroot/avatars
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
