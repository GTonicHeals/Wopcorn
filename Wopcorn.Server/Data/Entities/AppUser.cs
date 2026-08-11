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

    /// <summary>
    /// Whether a friend's suggestion goes straight onto the list it names (plan 10).
    ///
    /// Off by default, because the alternative is that signing up hands every
    /// friend write access to your queue. It is the <b>recipient's</b> setting and
    /// the sender can neither see nor influence it, which is why it sits here and
    /// not on <c>UserSummary</c>.
    /// </summary>
    public bool AutoAddSuggestions { get; set; }
}
