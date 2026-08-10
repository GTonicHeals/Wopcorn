using Microsoft.AspNetCore.Identity;

namespace Wopcorn.Server.Data.Entities;

public class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string? AvatarPath { get; set; }          // relative path under wwwroot/avatars
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// ISO-3166-1 alpha-2, and null until the user sets one (09).
    ///
    /// Streaming availability is region-scoped: with no region the answer is not
    /// approximate, it is wrong. So this is required state rather than a
    /// preference, and it is per user rather than per deployment — the alternative
    /// is wrong the first time someone travels or the group spans a border.
    /// </summary>
    public string? Region { get; set; }
}
