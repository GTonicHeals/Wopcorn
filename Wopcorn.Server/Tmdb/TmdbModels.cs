using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tmdb;

/// <summary>The three discovery feeds exposed by <c>GET /api/titles/discover/{feed}</c>.</summary>
public enum DiscoverFeed
{
    Popular,
    TopRated,
    NowPlaying,
}

public static class DiscoverFeeds
{
    /// <summary>
    /// Parses the wire value (<c>popular</c> | <c>top-rated</c> | <c>now-playing</c>).
    /// Anything else is a 404 at the controller, never a 400.
    /// </summary>
    public static bool TryParse(string? value, out DiscoverFeed feed)
    {
        switch (value)
        {
            case "popular":
                feed = DiscoverFeed.Popular;
                return true;
            case "top-rated":
                feed = DiscoverFeed.TopRated;
                return true;
            case "now-playing":
                feed = DiscoverFeed.NowPlaying;
                return true;
            default:
                feed = default;
                return false;
        }
    }

    public static string ToWire(this DiscoverFeed feed) => feed switch
    {
        DiscoverFeed.Popular => "popular",
        DiscoverFeed.TopRated => "top-rated",
        DiscoverFeed.NowPlaying => "now-playing",
        _ => throw new ArgumentOutOfRangeException(nameof(feed)),
    };

    /// <summary>
    /// The upstream path for one feed and one media type. There is no single TMDB
    /// feed spanning films and TV, so each of the three is really two — and
    /// <c>now_playing</c>'s TV counterpart is <c>on_the_air</c>, not a rename.
    /// </summary>
    public static string ToPath(this DiscoverFeed feed, MediaType mediaType) =>
        (feed, mediaType) switch
        {
            (DiscoverFeed.Popular, MediaType.Movie) => "movie/popular",
            (DiscoverFeed.TopRated, MediaType.Movie) => "movie/top_rated",
            (DiscoverFeed.NowPlaying, MediaType.Movie) => "movie/now_playing",
            (DiscoverFeed.Popular, MediaType.Series) => "tv/popular",
            (DiscoverFeed.TopRated, MediaType.Series) => "tv/top_rated",
            (DiscoverFeed.NowPlaying, MediaType.Series) => "tv/on_the_air",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType),
                "TMDB has no discovery feed for seasons."),
        };
}

// Upstream payloads. Property names are mapped with
// JsonNamingPolicy.SnakeCaseLower, so PosterPath reads "poster_path".

public record TmdbPage<T>(int Page, int TotalPages, int TotalResults, IReadOnlyList<T>? Results);

public record TmdbMovieSummary(
    int Id,
    string? Title,
    string? OriginalTitle,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? ReleaseDate,
    double? VoteAverage,
    IReadOnlyList<int>? GenreIds);

/// <summary>
/// One row of <c>/search/multi</c>. The endpoint returns films, series and
/// <b>people</b> in one relevance-ordered list, discriminated by
/// <see cref="MediaType"/>; people are discarded before anything else looks at
/// the page.
/// </summary>
/// <remarks>
/// TV rows name their fields differently from film rows — <c>name</c> for
/// <c>title</c>, <c>first_air_date</c> for <c>release_date</c> — so both spellings
/// are deserialized and <see cref="AsMovie"/>/<see cref="AsSeries"/> normalise
/// them into the one summary shape the cache understands.
/// </remarks>
public record TmdbMultiSummary(
    int Id,
    string? MediaType,
    string? Title,
    string? OriginalTitle,
    string? Name,
    string? OriginalName,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? ReleaseDate,
    string? FirstAirDate,
    double? VoteAverage,
    IReadOnlyList<int>? GenreIds)
{
    public bool IsMovie => string.Equals(MediaType, "movie", StringComparison.Ordinal);

    public bool IsSeries => string.Equals(MediaType, "tv", StringComparison.Ordinal);

    public TmdbMovieSummary AsMovie() =>
        new(Id, Title, OriginalTitle, Overview, PosterPath, BackdropPath,
            ReleaseDate, VoteAverage, GenreIds);

    public TmdbSeriesSummary AsSeries() =>
        new(Id, Name, OriginalName, Overview, PosterPath, BackdropPath,
            FirstAirDate, VoteAverage, GenreIds);
}

/// <summary>A series as it appears in a search or discovery page.</summary>
public record TmdbSeriesSummary(
    int Id,
    string? Name,
    string? OriginalName,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? FirstAirDate,
    double? VoteAverage,
    IReadOnlyList<int>? GenreIds);

public record TmdbGenre(int Id, string? Name);

public record TmdbCastMember(string? Name, string? Character, string? ProfilePath, int Order);

public record TmdbCrewMember(string? Name, string? Job);

public record TmdbCredits(IReadOnlyList<TmdbCastMember>? Cast, IReadOnlyList<TmdbCrewMember>? Crew);

public record TmdbMovieDetail(
    int Id,
    string? Title,
    string? OriginalTitle,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? ReleaseDate,
    double? VoteAverage,
    int? Runtime,
    IReadOnlyList<TmdbGenre>? Genres,
    TmdbCredits? Credits);

public record TmdbCreator(int Id, string? Name);

/// <summary>
/// The <c>seasons[]</c> entry on a series detail. Enough to write a season's
/// summary row without a request of its own, which is what keeps opening a series
/// at one upstream call rather than one per season.
/// </summary>
public record TmdbSeasonSummary(
    int Id,
    int SeasonNumber,
    string? Name,
    string? Overview,
    string? PosterPath,
    string? AirDate,
    int? EpisodeCount);

public record TmdbSeriesDetail(
    int Id,
    string? Name,
    string? OriginalName,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? FirstAirDate,
    double? VoteAverage,
    // Frequently empty — Breaking Bad returns []. A null runtime is the normal
    // outcome for a series, not an error.
    IReadOnlyList<int>? EpisodeRunTime,
    int? NumberOfEpisodes,
    int? NumberOfSeasons,
    IReadOnlyList<TmdbGenre>? Genres,
    IReadOnlyList<TmdbCreator>? CreatedBy,
    IReadOnlyList<TmdbSeasonSummary>? Seasons,
    TmdbCredits? Credits);

public record TmdbEpisode(int EpisodeNumber, string? Name, string? AirDate, int? Runtime);

public record TmdbSeasonDetail(
    int Id,
    int SeasonNumber,
    string? Name,
    string? Overview,
    string? PosterPath,
    string? AirDate,
    double? VoteAverage,
    IReadOnlyList<TmdbEpisode>? Episodes,
    TmdbCredits? Credits);

public record TmdbGenreList(IReadOnlyList<TmdbGenre>? Genres);

// --------------------------------------------------- watch providers (plan 09)

/// <summary>One service on one offer array of a region entry.</summary>
public record TmdbProviderOffer(int ProviderId, string? ProviderName, string? LogoPath, int DisplayPriority);

/// <summary>
/// What one region entry of <c>/watch/providers</c> carries. Every array is
/// optional and most are absent for most titles.
/// </summary>
public record TmdbRegionOffers(
    string? Link,
    IReadOnlyList<TmdbProviderOffer>? Flatrate,
    IReadOnlyList<TmdbProviderOffer>? Free,
    IReadOnlyList<TmdbProviderOffer>? Ads,
    IReadOnlyList<TmdbProviderOffer>? Rent,
    IReadOnlyList<TmdbProviderOffer>? Buy)
{
    /// <summary>The offer arrays paired with the kind each one means.</summary>
    public IEnumerable<(OfferKind Kind, IReadOnlyList<TmdbProviderOffer> Offers)> ByKind()
    {
        yield return (OfferKind.Flatrate, Flatrate ?? []);
        yield return (OfferKind.Free, Free ?? []);
        yield return (OfferKind.Ads, Ads ?? []);
        yield return (OfferKind.Rent, Rent ?? []);
        yield return (OfferKind.Buy, Buy ?? []);
    }
}

/// <summary>
/// <c>/movie/{id}/watch/providers</c> and its TV twin. <c>results</c> is keyed by
/// ISO-3166-1 region and the whole world arrives in one response whatever region
/// the caller cares about, which is why every region is stored (D-2).
/// </summary>
public record TmdbWatchProviders(int Id, IReadOnlyDictionary<string, TmdbRegionOffers>? Results);

/// <summary>One row of <c>/watch/providers/{movie|tv}?watch_region=XX</c>.</summary>
public record TmdbProviderDirectoryEntry(
    int ProviderId, string? ProviderName, string? LogoPath, int DisplayPriority);

public record TmdbProviderDirectory(IReadOnlyList<TmdbProviderDirectoryEntry>? Results);
