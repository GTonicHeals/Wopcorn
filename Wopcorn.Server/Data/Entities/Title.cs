namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// The local TMDB cache (FR-B6, FR-B7) — films, series and individual seasons,
/// all at the same grain. Each is a thing that can go on Watched, Watchlist or
/// the Queue, and each can be rated.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Key"/> is the primary key, and it is the <b>only</b> writable
/// identity on this row: <see cref="MediaType"/>, <see cref="TmdbId"/> and
/// <see cref="SeasonNumber"/> are set from a <see cref="TitleKey"/> through
/// <see cref="ApplyKey"/> and never the other way round. They exist as their own
/// columns so filtering and sorting can use them; the key stays authoritative so
/// the two can never disagree.
/// </para>
/// <para>
/// A string primary key rather than a composite <c>(MediaType, TmdbId,
/// SeasonNumber)</c>: three tables foreign-key to this one, and a three-column FK
/// would triple the join predicates, index definitions and EF configuration in
/// each of them — for a key that is never queried by its parts alone. The cost is
/// ~10 bytes instead of 4 across four indexes.
/// </para>
/// </remarks>
public class Title
{
    /// <summary>PK. <c>movie-603</c>, <c>tv-1396</c>, <c>tv-1396-s2</c>.</summary>
    public required string Key { get; set; }

    public MediaType MediaType { get; set; }

    /// <summary>The TMDB id — of the <b>series</b> when this row is a season.</summary>
    public int TmdbId { get; set; }

    public int? SeasonNumber { get; set; }

    /// <summary>A season's series. Null on films and series.</summary>
    public string? ParentKey { get; set; }

    public Title? Parent { get; set; }

    /// <summary>
    /// The display title. Called <c>Name</c> because <c>Title.Title</c> inside a
    /// class called <c>Title</c> is legal and unreadable; it maps to the existing
    /// <c>Title</c> column so the migration does not have to rewrite the data.
    /// </summary>
    public required string Name { get; set; }

    /// <summary><c>release_date</c>, <c>first_air_date</c> or <c>air_date</c>.</summary>
    public DateOnly? ReleaseDate { get; set; }

    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbVoteAverage { get; set; }

    /// <summary>
    /// Null is normal on series and seasons: TMDB's <c>episode_run_time</c> is an
    /// array and is frequently empty. See <c>TitleCacheService</c> for the rule.
    /// </summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>Series and seasons.</summary>
    public int? EpisodeCount { get; set; }

    /// <summary>Series only.</summary>
    public int? SeasonCount { get; set; }

    public string? Overview { get; set; }

    /// <summary>Films only — TMDB has no series-wide director.</summary>
    public string? Director { get; set; }

    /// <summary>Series only: <c>created_by[].name</c>, serialized.</summary>
    public string? CreatorsJson { get; set; }

    /// <summary>Serialized cast array, max 12.</summary>
    public string? CastJson { get; set; }

    public DateTimeOffset SummaryFetchedAt { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }

    public ICollection<TitleGenre> Genres { get; set; } = [];

    /// <summary>The row's identity, parsed back out of the stored key.</summary>
    public TitleKey TitleKey => TitleKey.Parse(Key);

    /// <summary>
    /// The one way the identity columns are written. Everything else about a row
    /// is upstream data; this is the part that has to stay internally consistent.
    /// </summary>
    public void ApplyKey(TitleKey key)
    {
        Key = key.Value;
        MediaType = key.MediaType;
        TmdbId = key.TmdbId;
        SeasonNumber = key.SeasonNumber;
        ParentKey = key.Parent?.Value;
    }

    /// <summary>Creates a row already carrying a consistent identity.</summary>
    public static Title New(TitleKey key, string name, DateTimeOffset fetchedAt)
    {
        var title = new Title { Key = key.Value, Name = name, SummaryFetchedAt = fetchedAt };
        title.ApplyKey(key);
        return title;
    }
}
