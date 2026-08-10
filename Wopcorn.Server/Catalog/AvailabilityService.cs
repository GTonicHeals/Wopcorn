using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Catalog;

/// <summary>One offer kind and the services offering it, ready for the wire.</summary>
public record AvailabilityOffer(OfferKind Kind, IReadOnlyList<WatchProvider> Providers);

/// <summary>
/// What we know about where one title can be watched in one region.
/// <see cref="FetchedAt"/> is null when we have never looked, which the client
/// renders as "unknown" — distinct from a fetch that came back with nothing.
/// </summary>
public record AvailabilitySnapshot(
    string TitleKey,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<AvailabilityOffer> Offers);

/// <summary>
/// The only type that writes <c>TitleAvailability</c>, <c>TitleOffer</c> and
/// <c>WatchProviders</c>, and the only caller of <see cref="ITmdbClient"/> for
/// provider data — the same shape <see cref="TitleCacheService"/> has for the
/// catalog.
///
/// Nothing here throws. Availability decorates a page; it never fails one, so an
/// upstream outage degrades to stale rows or to "unknown" and never to a 503
/// (FR-B8, NFR-10).
/// </summary>
public sealed class AvailabilityService(
    WopcornDbContext db,
    ITmdbClient tmdb,
    ILogger<AvailabilityService> logger)
{
    /// <summary>
    /// Catalogues churn weekly, so this is a day rather than the seven
    /// <see cref="TitleCacheService.DetailTtl"/> gives a detail row.
    /// </summary>
    public static readonly TimeSpan AvailabilityTtl = TimeSpan.FromHours(24);

    /// <summary>Pure staleness policy, exposed so the TTL boundary is unit-testable.</summary>
    public static bool IsFresh(DateTimeOffset fetchedAt, DateTimeOffset now) =>
        now - fetchedAt < AvailabilityTtl;

    /// <summary>
    /// The viewer's region and services, read once per request. Every card page
    /// asks for them, and they do not change inside one.
    /// </summary>
    private (Guid UserId, string? Region, int[] ProviderIds)? _viewer;

    // ------------------------------------------------------------------ region

    /// <summary>
    /// A region is two letters, upper-cased. Anything else is not a region, and a
    /// wrong region is a wrong answer rather than an approximate one.
    /// </summary>
    public static bool TryNormalizeRegion(string? value, out string region)
    {
        region = string.Empty;
        var trimmed = value?.Trim();
        if (trimmed is not { Length: 2 } || !trimmed.All(char.IsAsciiLetter))
        {
            return false;
        }

        region = trimmed.ToUpperInvariant();
        return true;
    }

    // ------------------------------------------------------------ the one title

    /// <summary>
    /// Everything known about a title in a region, refreshing from TMDB when the
    /// stored copy has aged past <see cref="AvailabilityTtl"/>.
    ///
    /// A season resolves to its series: TMDB exposes providers for films and series
    /// only, and the honest answer to "where can I watch season 2" is where the
    /// show is carried.
    /// </summary>
    public async Task<AvailabilitySnapshot> GetAsync(
        TitleKey key, string region, CancellationToken ct)
    {
        var resolved = Resolve(key);
        var now = DateTimeOffset.UtcNow;

        var stored = await LoadAsync(resolved.Value, region, ct);
        if (stored is { FetchedAt: { } fetchedAt } && IsFresh(fetchedAt, now))
        {
            return stored;
        }

        TmdbWatchProviders? payload;
        try
        {
            payload = await tmdb.GetWatchProvidersAsync(resolved.MediaType, resolved.TmdbId, ct);
        }
        catch (TmdbUnavailableException)
        {
            // Stale rows beat an empty block, and an empty block beats an error.
            logger.LogWarning(
                "Watch providers for {Key} are unavailable; serving what is stored.", resolved.Value);
            return stored ?? Unknown(resolved.Value, region);
        }

        await ReplaceAsync(resolved.Value, region, payload, now, ct);

        return await LoadAsync(resolved.Value, region, ct)
               ?? Unknown(resolved.Value, region);
    }

    /// <summary>
    /// The warmer's entry point: refresh one title unconditionally, without
    /// projecting anything back. Same failure contract — it never throws.
    /// </summary>
    public async Task<bool> RefreshAsync(TitleKey key, string region, CancellationToken ct)
    {
        var resolved = Resolve(key);

        try
        {
            var payload = await tmdb.GetWatchProvidersAsync(resolved.MediaType, resolved.TmdbId, ct);
            await ReplaceAsync(resolved.Value, region, payload, DateTimeOffset.UtcNow, ct);
            return true;
        }
        catch (TmdbUnavailableException)
        {
            logger.LogWarning("Warming {Key} in {Region} failed; leaving what is stored.",
                resolved.Value, region);
            return false;
        }
    }

    /// <summary>
    /// Rewrites every region we hold for one title from a single upstream payload.
    ///
    /// TMDB answers with the whole world whatever region the caller cares about, so
    /// storing only the region asked for would guarantee a second request the
    /// moment anyone sets a different one (D-2). A null payload is a 404 from the
    /// providers endpoint — "no data for this title", which is an answer worth
    /// recording so it is not asked again for a day.
    /// </summary>
    private async Task ReplaceAsync(
        string titleKey,
        string requestedRegion,
        TmdbWatchProviders? payload,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existingOffers = await db.TitleOffers.Where(o => o.TitleKey == titleKey).ToListAsync(ct);
        db.TitleOffers.RemoveRange(existingOffers);

        var existingRows = await db.TitleAvailability
            .Where(a => a.TitleKey == titleKey)
            .ToDictionaryAsync(a => a.Region, StringComparer.Ordinal, ct);

        // The region that was actually asked for, whether or not the payload
        // mentions it. Without this row a title carried by nobody in Belgium has no
        // record of ever having been looked at, and is re-fetched on every request
        // forever — the exact distinction this table exists to draw.
        Upsert(existingRows, titleKey, requestedRegion, now);

        var regions = payload?.Results ?? new Dictionary<string, TmdbRegionOffers>();

        foreach (var (rawRegion, offers) in regions)
        {
            if (!TryNormalizeRegion(rawRegion, out var region))
            {
                continue;
            }

            var row = Upsert(existingRows, titleKey, region, now);
            row.JustWatchLink = offers.Link;

            foreach (var (kind, entries) in offers.ByKind())
            {
                foreach (var entry in entries)
                {
                    var provider = await EnsureProviderAsync(entry, ct);
                    if (provider is null)
                    {
                        continue;
                    }

                    db.TitleOffers.Add(new TitleOffer
                    {
                        TitleKey = titleKey,
                        Region = region,
                        ProviderId = provider.TmdbProviderId,
                        Kind = kind,
                    });
                }
            }
        }

        // Regions the payload no longer mentions are still regions we have looked
        // at — their rows stay, stamped, with no offers beside them. That is the
        // "we looked, and there is nothing" answer, and it is what stops a title
        // carried nowhere in Belgium from being re-fetched on every request.
        foreach (var row in existingRows.Values)
        {
            row.FetchedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private TitleAvailability Upsert(
        Dictionary<string, TitleAvailability> existing,
        string titleKey,
        string region,
        DateTimeOffset now)
    {
        if (!existing.TryGetValue(region, out var row))
        {
            row = new TitleAvailability { TitleKey = titleKey, Region = region };
            db.TitleAvailability.Add(row);
            existing[region] = row;
        }

        row.FetchedAt = now;
        return row;
    }

    private async Task<AvailabilitySnapshot?> LoadAsync(
        string titleKey, string region, CancellationToken ct)
    {
        var row = await db.TitleAvailability
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TitleKey == titleKey && a.Region == region, ct);

        if (row is null)
        {
            return null;
        }

        var offers = await db.TitleOffers
            .AsNoTracking()
            .Where(o => o.TitleKey == titleKey && o.Region == region)
            .Select(o => new { o.Kind, o.Provider })
            .ToListAsync(ct);

        var grouped = OfferKinds.InRenderOrder
            .Select(kind => new AvailabilityOffer(
                kind,
                offers
                    .Where(o => o.Kind == kind)
                    .Select(o => o.Provider)
                    .OrderBy(p => p.DisplayPriority)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .Where(group => group.Providers.Count > 0)
            .ToArray();

        return new AvailabilitySnapshot(titleKey, region, row.FetchedAt, row.JustWatchLink, grouped);
    }

    private static AvailabilitySnapshot Unknown(string titleKey, string region) =>
        new(titleKey, region, FetchedAt: null, Link: null, Offers: []);

    // --------------------------------------------------------- the directory

    /// <summary>
    /// The services TMDB publishes for one region, mirrored into
    /// <c>WatchProviders</c> — the union of the film and TV directories, exactly as
    /// <see cref="GenreCatalogService"/> mirrors the two genre lists.
    /// </summary>
    /// <remarks>
    /// The merge against <c>db.WatchProviders.Local</c> is load-bearing for the same
    /// reason it is there: a provider id appears on <b>both</b> upstream lists, so
    /// without it the TV pass adds a second entity with a key the film pass already
    /// tracks and EF refuses to track it. This is the identical bug
    /// <see cref="GenreCatalogService"/> documents.
    /// </remarks>
    public async Task<IReadOnlyList<WatchProvider>> EnsureDirectoryAsync(
        string region, CancellationToken ct)
    {
        var movies = await SafeDirectoryAsync(MediaType.Movie, region, ct);
        var tv = await SafeDirectoryAsync(MediaType.Series, region, ct);

        if (movies.Count == 0 && tv.Count == 0)
        {
            // Neither list could be consulted. The providers we have actually seen
            // carrying titles in this region are region-scoped local knowledge and
            // beat listing the whole global table.
            return await KnownInRegionAsync(region, ct);
        }

        var upstream = movies.Concat(tv).ToList();
        var ids = upstream.Select(p => p.ProviderId).ToHashSet();

        var rows = await db.WatchProviders
            .Where(p => ids.Contains(p.TmdbProviderId))
            .ToDictionaryAsync(p => p.TmdbProviderId, ct);

        foreach (var tracked in db.WatchProviders.Local.Where(p => ids.Contains(p.TmdbProviderId)))
        {
            rows.TryAdd(tracked.TmdbProviderId, tracked);
        }

        foreach (var entry in upstream)
        {
            Apply(rows, entry.ProviderId, entry.ProviderName, entry.LogoPath, entry.DisplayPriority);
        }

        await db.SaveChangesAsync(ct);

        return rows.Values
            .OrderBy(p => p.DisplayPriority)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Whether an id is a service the directory for this region knows.</summary>
    public async Task<HashSet<int>> DirectoryIdsAsync(string region, CancellationToken ct) =>
        [.. (await EnsureDirectoryAsync(region, ct)).Select(p => p.TmdbProviderId)];

    private async Task<IReadOnlyList<WatchProvider>> KnownInRegionAsync(
        string region, CancellationToken ct) =>
        await db.TitleOffers
            .AsNoTracking()
            .Where(o => o.Region == region)
            .Select(o => o.Provider)
            .Distinct()
            .OrderBy(p => p.DisplayPriority)
            .ThenBy(p => p.Name)
            .ToArrayAsync(ct);

    private async Task<IReadOnlyList<TmdbProviderDirectoryEntry>> SafeDirectoryAsync(
        MediaType mediaType, string region, CancellationToken ct)
    {
        try
        {
            return await tmdb.GetProviderDirectoryAsync(mediaType, region, ct);
        }
        catch (TmdbUnavailableException)
        {
            logger.LogWarning(
                "The {MediaType} provider directory for {Region} is unavailable.", mediaType, region);
            return [];
        }
    }

    /// <summary>
    /// Adds or refreshes one provider row. Offers carry the same four fields the
    /// directory does, so a title fetch keeps the mirror current on its own — which
    /// is what lets availability work before anyone opens the settings screen.
    /// </summary>
    private async Task<WatchProvider?> EnsureProviderAsync(
        TmdbProviderOffer offer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(offer.ProviderName))
        {
            return null;
        }

        var row = db.WatchProviders.Local.FirstOrDefault(p => p.TmdbProviderId == offer.ProviderId)
                  ?? await db.WatchProviders.FirstOrDefaultAsync(
                      p => p.TmdbProviderId == offer.ProviderId, ct);

        if (row is null)
        {
            row = new WatchProvider { TmdbProviderId = offer.ProviderId, Name = offer.ProviderName };
            db.WatchProviders.Add(row);
        }

        row.Name = offer.ProviderName;
        row.LogoPath = offer.LogoPath ?? row.LogoPath;
        row.DisplayPriority = offer.DisplayPriority;
        return row;
    }

    private void Apply(
        Dictionary<int, WatchProvider> rows, int id, string? name, string? logoPath, int priority)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!rows.TryGetValue(id, out var row))
        {
            row = new WatchProvider { TmdbProviderId = id, Name = name };
            db.WatchProviders.Add(row);
            rows[id] = row;
        }

        row.Name = name;
        row.LogoPath = logoPath ?? row.LogoPath;
        row.DisplayPriority = priority;
    }

    // ------------------------------------------------------------- the cards

    /// <summary>
    /// The card path: which of <b>the viewer's own</b> services carry each of these
    /// titles on subscription, in their region.
    ///
    /// One grouped query for a whole page, never one per title (NFR-2) — and zero
    /// queries over the offer table at all when the viewer has configured no
    /// services, which is every viewer until they visit settings.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int[]>> AvailableOnAsync(
        Guid userId, IReadOnlyCollection<string> titleKeys, CancellationToken ct)
    {
        var empty = new Dictionary<string, int[]>(StringComparer.Ordinal);
        if (titleKeys.Count == 0)
        {
            return empty;
        }

        var (region, providerIds) = await ViewerAsync(userId, ct);
        if (region is null || providerIds.Length == 0)
        {
            return empty;
        }

        // Seasons resolve to their series on the way in and back to their own key on
        // the way out, so a season card badges what its show is carried on.
        var lookup = titleKeys
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(k => k, ResolveKey, StringComparer.Ordinal);

        var wanted = lookup.Values.Distinct(StringComparer.Ordinal).ToList();

        var rows = await db.TitleOffers
            .AsNoTracking()
            .Where(o => o.Region == region
                        && o.Kind == OfferKind.Flatrate
                        && providerIds.Contains(o.ProviderId)
                        && wanted.Contains(o.TitleKey))
            .Select(o => new { o.TitleKey, o.ProviderId })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return empty;
        }

        var byResolved = rows
            .GroupBy(r => r.TitleKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ProviderId).Distinct().Order().ToArray(),
                StringComparer.Ordinal);

        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (key, resolved) in lookup)
        {
            if (byResolved.TryGetValue(resolved, out var ids))
            {
                result[key] = ids;
            }
        }

        return result;
    }

    /// <summary>The viewer's region and services, loaded once per request scope.</summary>
    public async Task<(string? Region, int[] ProviderIds)> ViewerAsync(
        Guid userId, CancellationToken ct)
    {
        if (_viewer is { } cached && cached.UserId == userId)
        {
            return (cached.Region, cached.ProviderIds);
        }

        var region = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Region)
            .FirstOrDefaultAsync(ct);

        var providerIds = region is null
            ? []
            : await db.UserWatchProviders
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.ProviderId)
                .OrderBy(id => id)
                .ToArrayAsync(ct);

        _viewer = (userId, region, providerIds);
        return (region, providerIds);
    }

    /// <summary>Forgets the cached viewer after <c>PUT /api/me/services</c>.</summary>
    public void InvalidateViewer() => _viewer = null;

    /// <summary>
    /// Replaces the viewer's whole set of services, like the queue order and the
    /// favourites showcase. Callers validate the ids first — an unknown one is a
    /// <c>400</c>, because a silently dropped service is a filter that lies.
    /// </summary>
    public async Task SetServicesAsync(
        Guid userId, string region, IReadOnlyCollection<int> providerIds, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.Region = region;

        var existing = await db.UserWatchProviders.Where(u => u.UserId == userId).ToListAsync(ct);
        db.UserWatchProviders.RemoveRange(existing);
        await db.SaveChangesAsync(ct);

        foreach (var id in providerIds.Distinct())
        {
            db.UserWatchProviders.Add(new UserWatchProvider { UserId = userId, ProviderId = id });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        InvalidateViewer();
    }

    // ------------------------------------------------------------------ keys

    /// <summary>
    /// A season's providers are its series'. A season may never exist without its
    /// series row (08, task 3), so this is a rename rather than a lookup.
    /// </summary>
    public static TitleKey Resolve(TitleKey key) => key.Parent ?? key;

    private static string ResolveKey(string key) =>
        TitleKey.TryParse(key, out var parsed) ? Resolve(parsed).Value : key;
}
