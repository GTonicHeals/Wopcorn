using System.Globalization;

namespace Wopcorn.Server.Data.Entities;

/// <summary>What a <see cref="Title"/> row describes.</summary>
public enum MediaType
{
    Movie = 1,
    Series = 2,
    Season = 3,
}

public static class MediaTypes
{
    /// <summary>The contract's wire strings (<c>movie</c> | <c>series</c> | <c>season</c>).</summary>
    public static string ToWire(this MediaType type) => type switch
    {
        MediaType.Movie => "movie",
        MediaType.Series => "series",
        MediaType.Season => "season",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Parses a <c>type</c> query value. Unknown values are <c>false</c> so a caller
    /// can ignore them rather than 400 — the same rule the sort and filter values
    /// follow (FR-C5).
    /// </summary>
    public static bool TryParse(string? value, out MediaType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "movie":
                type = MediaType.Movie;
                return true;
            case "series":
                type = MediaType.Series;
                return true;
            case "season":
                type = MediaType.Season;
                return true;
            default:
                type = default;
                return false;
        }
    }
}

/// <summary>
/// The identifier of a title, in the one format the whole system uses — primary
/// key, wire identifier, and URL segment alike.
///
/// <code>
/// movie-603          a film
/// tv-1396            a series
/// tv-1396-s2         season 2 of that series
/// </code>
///
/// TMDB's film and TV ids are separate namespaces and they collide: 1396 is
/// <i>Mirror</i> (1975) as a movie and <i>Breaking Bad</i> as a series. Carrying
/// the media type in the key is what keeps the two apart everywhere — in the
/// catalog table, in three foreign keys, and in every route.
///
/// This type is the <b>only</b> place the format is known on the server. Nothing
/// else concatenates or splits these strings; a <see cref="Title"/>'s parts are
/// written from a key, never the reverse.
/// </summary>
public readonly record struct TitleKey
{
    private TitleKey(MediaType mediaType, int tmdbId, int? seasonNumber, string value)
    {
        MediaType = mediaType;
        TmdbId = tmdbId;
        SeasonNumber = seasonNumber;
        Value = value;
    }

    public MediaType MediaType { get; }

    /// <summary>The TMDB id — of the <b>series</b> when this is a season.</summary>
    public int TmdbId { get; }

    /// <summary>TMDB's own season number; <c>0</c> is the specials season.</summary>
    public int? SeasonNumber { get; }

    /// <summary>The canonical string. This is what is stored and what is routed.</summary>
    public string Value { get; }

    public bool IsSeason => MediaType == MediaType.Season;

    /// <summary>A season's series; <c>null</c> for anything else.</summary>
    public TitleKey? Parent => IsSeason ? ForSeries(TmdbId) : null;

    public static TitleKey ForMovie(int tmdbId) =>
        new(MediaType.Movie, tmdbId, null, Format(MediaType.Movie, tmdbId, null));

    public static TitleKey ForSeries(int tmdbId) =>
        new(MediaType.Series, tmdbId, null, Format(MediaType.Series, tmdbId, null));

    public static TitleKey ForSeason(int seriesTmdbId, int seasonNumber) =>
        new(MediaType.Season, seriesTmdbId, seasonNumber,
            Format(MediaType.Season, seriesTmdbId, seasonNumber));

    /// <summary>
    /// The general constructor. A season number is required for
    /// <see cref="MediaType.Season"/> and rejected for anything else.
    /// </summary>
    public static TitleKey For(MediaType mediaType, int tmdbId, int? seasonNumber = null) =>
        mediaType switch
        {
            MediaType.Movie when seasonNumber is null => ForMovie(tmdbId),
            MediaType.Series when seasonNumber is null => ForSeries(tmdbId),
            MediaType.Season when seasonNumber is { } number => ForSeason(tmdbId, number),
            _ => throw new ArgumentException(
                $"{mediaType} does not take a season number of {seasonNumber?.ToString(CultureInfo.InvariantCulture) ?? "null"}.",
                nameof(seasonNumber)),
        };

    /// <summary>
    /// Parses the canonical form. Hand-written rather than a regular expression:
    /// this runs on every routed request, and the grammar is small enough that the
    /// scan is both faster and easier to read than the pattern would be.
    ///
    /// Deliberately strict — no leading <c>+</c>, no leading zeros beyond a bare
    /// <c>0</c>, no whitespace. Two spellings of one key would mean two rows.
    /// </summary>
    public static bool TryParse(string? value, out TitleKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var span = value.AsSpan();

        if (span.StartsWith("movie-", StringComparison.Ordinal))
        {
            if (!TryNumber(span[6..], out var movieId))
            {
                return false;
            }

            key = ForMovie(movieId);
            return true;
        }

        if (!span.StartsWith("tv-", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = span[3..];
        var seasonMark = rest.IndexOf("-s", StringComparison.Ordinal);

        if (seasonMark < 0)
        {
            if (!TryNumber(rest, out var seriesId))
            {
                return false;
            }

            key = ForSeries(seriesId);
            return true;
        }

        if (!TryNumber(rest[..seasonMark], out var parentId) ||
            !TryNumber(rest[(seasonMark + 2)..], out var seasonNumber))
        {
            return false;
        }

        key = ForSeason(parentId, seasonNumber);
        return true;
    }

    /// <summary>
    /// The parsing form for code that has already established the key is well
    /// formed — a row read back out of the database, say. Anything arriving off the
    /// wire goes through <see cref="TryParse"/>, because a malformed key there is a
    /// <c>400</c>, not an exception.
    /// </summary>
    public static TitleKey Parse(string? value) =>
        TryParse(value, out var key)
            ? key
            : throw new FormatException($"'{value}' is not a title key.");

    public override string ToString() => Value;

    public static implicit operator string(TitleKey key) => key.Value;

    private static string Format(MediaType mediaType, int tmdbId, int? seasonNumber) => mediaType switch
    {
        MediaType.Movie => $"movie-{tmdbId}",
        MediaType.Series => $"tv-{tmdbId}",
        MediaType.Season => $"tv-{tmdbId}-s{seasonNumber}",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    /// <summary>
    /// A run of digits and nothing else, with no sign and no redundant leading
    /// zero. <c>int.TryParse</c> alone would accept <c>"+7"</c> and <c>" 7"</c>,
    /// which are second spellings of a key that already has one.
    /// </summary>
    private static bool TryNumber(ReadOnlySpan<char> span, out int value)
    {
        value = 0;
        if (span.Length == 0 || (span.Length > 1 && span[0] == '0'))
        {
            return false;
        }

        foreach (var c in span)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
