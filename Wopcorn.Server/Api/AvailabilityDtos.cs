using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

// API-CONTRACT.md "Streaming availability".

/// <summary>One service, as the client renders it.</summary>
public record WatchProviderDto(int Id, string Name, string? LogoPath)
{
    public static WatchProviderDto From(WatchProvider provider) =>
        new(provider.TmdbProviderId, provider.Name, provider.LogoPath);
}

public record OfferGroupDto(string Kind, IReadOnlyList<WatchProviderDto> Providers);

/// <summary>
/// Where one title can be watched in one region.
/// </summary>
/// <param name="FetchedAt">
/// Null means we have never looked, which the client renders as "unknown" — not
/// the same answer as a fetch that came back with nothing, which is an empty
/// <paramref name="Offers"/> beside a non-null timestamp.
/// </param>
public record TitleAvailabilityDto(
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<OfferGroupDto> Offers)
{
    public static TitleAvailabilityDto From(AvailabilitySnapshot snapshot) =>
        new(snapshot.Region,
            snapshot.FetchedAt,
            snapshot.Link,
            snapshot.Offers
                .Select(o => new OfferGroupDto(
                    o.Kind.ToWire(),
                    o.Providers.Select(WatchProviderDto.From).ToArray()))
                .ToArray());
}

/// <summary>Body and response of <c>PUT /api/me/services</c>.</summary>
public record ServicesDto(string Region, IReadOnlyList<int> ProviderIds);

/// <summary>
/// <c>GET /api/me</c> — the signed-in user's own view of themself.
/// <see cref="UserSummary"/> deliberately does not carry these: it also describes
/// friends, and a friend's region and subscriptions are their business.
/// </summary>
/// <param name="AutoAddSuggestions">
/// Whether a friend's suggestion goes straight onto the list it names (plan 10).
/// It is here and not on <see cref="UserSummary"/> for the same reason
/// <paramref name="Region"/> is: whether you auto-accept someone's suggestions is
/// not something they get to see.
/// </param>
public record MeDto(
    string Id,
    string DisplayName,
    string? AvatarUrl,
    string? Region,
    IReadOnlyList<int> ProviderIds,
    bool AutoAddSuggestions);

/// <summary>Body and response of <c>PUT /api/me/preferences</c>.</summary>
public record PreferencesDto(bool AutoAddSuggestions);
