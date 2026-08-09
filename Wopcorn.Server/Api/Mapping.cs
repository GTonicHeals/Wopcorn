using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

/// <summary>API-CONTRACT.md "Shared DTOs" — <c>UserSummary</c>.</summary>
public record UserSummary(string Id, string DisplayName, string? AvatarUrl);

public static class Mapping
{
    public static UserSummary ToSummary(this AppUser user) =>
        new(user.Id.ToString(),
            user.DisplayName,
            string.IsNullOrWhiteSpace(user.AvatarPath) ? null : "/" + user.AvatarPath.TrimStart('/'));
}
