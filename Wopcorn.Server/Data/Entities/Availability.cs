namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// How a title is offered by one service, in TMDB's own vocabulary.
///
/// Stored as an int so the ordering is stable across renames, and declared in the
/// order the detail page renders: what is included first, what costs money last.
/// </summary>
public enum OfferKind
{
    /// <summary>Included with a subscription. The only kind <c>availableOn</c> reports.</summary>
    Flatrate = 1,
    Free = 2,

    /// <summary>Free with advertising.</summary>
    Ads = 3,
    Rent = 4,
    Buy = 5,
}

public static class OfferKinds
{
    /// <summary>The contract's wire strings.</summary>
    public static string ToWire(this OfferKind kind) => kind switch
    {
        OfferKind.Flatrate => "flatrate",
        OfferKind.Free => "free",
        OfferKind.Ads => "ads",
        OfferKind.Rent => "rent",
        OfferKind.Buy => "buy",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Every kind, in the order the detail page renders them.</summary>
    public static readonly IReadOnlyList<OfferKind> InRenderOrder =
    [
        OfferKind.Flatrate,
        OfferKind.Free,
        OfferKind.Ads,
        OfferKind.Rent,
        OfferKind.Buy,
    ];
}

/// <summary>
/// One streaming service, mirroring TMDB's provider directory the way
/// <see cref="Genre"/> mirrors its genre lists.
/// </summary>
/// <remarks>
/// The directory is published per region and per media type, and the same provider
/// id appears on both the movie and TV lists — which is the same merge hazard
/// <c>GenreCatalogService</c> documents, and it recurs here verbatim if the write
/// path forgets to check the change tracker as well as the table.
/// </remarks>
public class WatchProvider
{
    /// <summary>PK — TMDB's <c>provider_id</c>.</summary>
    public int TmdbProviderId { get; set; }

    public required string Name { get; set; }

    /// <summary>Bare TMDB path, rendered through <c>imageBaseUrl</c> like a poster.</summary>
    public string? LogoPath { get; set; }

    /// <summary>
    /// TMDB's own ordering hint. The settings grid sorts by it so the eight services
    /// someone might plausibly have are above the fold and the long tail is not.
    /// </summary>
    public int DisplayPriority { get; set; }
}

/// <summary>
/// One row per <c>(title, region)</c> we have <b>asked</b> about.
///
/// Its existence is the answer to "have we looked?", and zero
/// <see cref="TitleOffer"/> rows beside it means "we looked, and there is nothing"
/// — a distinction <c>TitleCacheService</c> already draws for genres and that
/// matters more here: a title genuinely carried by no service in Belgium must not
/// be re-fetched on every request forever.
/// </summary>
public class TitleAvailability
{
    /// <summary>PK part, FK → <c>Titles.Key</c>. Never a season — see AvailabilityService.</summary>
    public required string TitleKey { get; set; }

    public Title Title { get; set; } = null!;

    /// <summary>PK part. ISO-3166-1 alpha-2, upper case.</summary>
    public required string Region { get; set; }

    /// <summary>JustWatch's page for this title in this region, verbatim from TMDB.</summary>
    public string? JustWatchLink { get; set; }

    /// <summary>
    /// When this <c>(title, region)</c> was last asked about. The warmer orders by
    /// it, so it <b>must</b> carry <c>UtcInstantConverter</c> — SQLite's provider
    /// throws on <c>ORDER BY</c> over a <see cref="DateTimeOffset"/>, at query time
    /// rather than at build time.
    /// </summary>
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// One service carrying one title, one way, in one region. Normalised rather than
/// a JSON blob because the Queue filter is a join over it (NFR-7, D-1).
/// </summary>
public class TitleOffer
{
    public required string TitleKey { get; set; }

    public required string Region { get; set; }

    public int ProviderId { get; set; }

    public WatchProvider Provider { get; set; } = null!;

    public OfferKind Kind { get; set; }
}

/// <summary>
/// A service the user says they pay for. Replaced wholesale by
/// <c>PUT /api/me/services</c>, like the favourites showcase and the queue order.
/// </summary>
public class UserWatchProvider
{
    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public int ProviderId { get; set; }

    public WatchProvider Provider { get; set; } = null!;
}
