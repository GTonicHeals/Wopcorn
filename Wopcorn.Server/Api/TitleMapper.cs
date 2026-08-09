using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Api;

// API-CONTRACT.md "Shared DTOs". TitleDetail derives from TitleCard so the "&" in
// the contract cannot silently drift; both serialize as one flat JSON object.

public record ListMembership(bool Watched, bool Watchlist, bool Queue)
{
    public static readonly ListMembership None = new(false, false, false);
}

/// <summary>The unit rendered in every grid, search result, and list row.</summary>
/// <param name="SeasonProgress">
/// Series only, and null until at least one season has been watched. It lives on
/// the card rather than only on the detail because a series card in a grid has to
/// be able to say "3 / 5 seasons" without opening it; the cost is one extra
/// grouped query per page, not per title.
/// </param>
public record TitleCard(
    string Key,
    string MediaType,
    int TmdbId,
    int? SeasonNumber,
    string? ParentKey,
    string Title,
    int? ReleaseYear,
    string? PosterPath,
    double? TmdbVoteAverage,
    int? RuntimeMinutes,
    int? EpisodeCount,
    int? SeasonCount,
    SeasonProgressDto? SeasonProgress,
    IReadOnlyList<int> GenreIds,
    ListMembership Lists,
    int? MyRating);

public record GenreDto(int Id, string Name, IReadOnlyList<string> MediaTypes);

public record CastMemberDto(string Name, string? Character, string? ProfilePath);

public record FriendWatchedDto(UserSummary User, int? Rating);

/// <summary>
/// One row of a series' Seasons section. Carries the requester's own membership
/// and rating, so a season can be toggled and rated from the series screen
/// without a request per season.
/// </summary>
public record SeasonSummaryDto(
    string Key,
    int SeasonNumber,
    string Name,
    int? EpisodeCount,
    string? AirDate,
    string? PosterPath,
    ListMembership Lists,
    int? MyRating);

/// <summary>
/// FR-C-adjacent, added by plan 08: how much of a series the requester has
/// watched. Not a cascade — the seasons and the series are independent entries,
/// and this is the honest thing to render instead of inventing a rule.
/// </summary>
public record SeasonProgressDto(int Watched, int Total);

public record TitleDetail(
    string Key,
    string MediaType,
    int TmdbId,
    int? SeasonNumber,
    string? ParentKey,
    string Title,
    int? ReleaseYear,
    string? PosterPath,
    double? TmdbVoteAverage,
    int? RuntimeMinutes,
    int? EpisodeCount,
    int? SeasonCount,
    SeasonProgressDto? SeasonProgress,
    IReadOnlyList<int> GenreIds,
    ListMembership Lists,
    int? MyRating,
    string? BackdropPath,
    string? Overview,
    string? ReleaseDate,
    IReadOnlyList<GenreDto> Genres,
    string? Director,
    IReadOnlyList<string> Creators,
    IReadOnlyList<CastMemberDto> Cast,
    IReadOnlyList<SeasonSummaryDto> Seasons,
    IReadOnlyList<FriendWatchedDto> FriendsWatched,
    bool Stale)
    : TitleCard(Key, MediaType, TmdbId, SeasonNumber, ParentKey, Title, ReleaseYear, PosterPath,
        TmdbVoteAverage, RuntimeMinutes, EpisodeCount, SeasonCount, SeasonProgress, GenreIds,
        Lists, MyRating);

/// <summary>A page of <see cref="TitleCard"/>s — the search and discover response.</summary>
public record TitlePage(int Page, int TotalPages, int TotalResults, IReadOnlyList<TitleCard> Results);

/// <summary>
/// Turns cached <see cref="Title"/> rows into the wire DTOs, and loads the
/// per-user decoration (<c>lists</c>, <c>myRating</c>) for a whole page in one
/// query — never per title (NFR-2, FR-C3).
/// </summary>
public sealed class TitleMapper(WopcornDbContext db)
{
    /// <summary>Title.CastJson is our own storage format, so it uses web defaults.</summary>
    private static readonly JsonSerializerOptions CastJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The decoration for one page, keyed by title key.
    /// </summary>
    /// <param name="WatchedSeasons">
    /// How many of this title's seasons the viewer has watched — meaningful only
    /// on a series, and zero everywhere else. It rides along here rather than being
    /// fetched per title so a grid of series still costs a constant number of
    /// queries (NFR-2).
    /// </param>
    public record UserContext(ListMembership Lists, int? Rating, int WatchedSeasons = 0)
    {
        public static readonly UserContext None = new(ListMembership.None, null);
    }

    public async Task<Dictionary<string, UserContext>> LoadUserContextAsync(
        Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return new Dictionary<string, UserContext>(StringComparer.Ordinal);
        }

        // One query for the whole page.
        var rows = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && keys.Contains(e.TitleKey))
            .Select(e => new { e.TitleKey, e.Kind, e.Rating })
            .ToListAsync(ct);

        // One more for season progress across every series on the page — grouped in
        // the database, never a query per series. Seasons are independent entries,
        // so this counts them; it never implies anything about the series' own.
        var seasonsWatched = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.Kind == ListKind.Watched
                        && e.Title.ParentKey != null
                        && keys.Contains(e.Title.ParentKey))
            .GroupBy(e => e.Title.ParentKey!)
            .Select(g => new { ParentKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ParentKey, g => g.Count, StringComparer.Ordinal, ct);

        var context = rows
            .GroupBy(r => r.TitleKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new UserContext(
                    new ListMembership(
                        g.Any(r => r.Kind == ListKind.Watched),
                        g.Any(r => r.Kind == ListKind.Watchlist),
                        g.Any(r => r.Kind == ListKind.Queue)),
                    g.Where(r => r.Kind == ListKind.Watched)
                        .Select(r => r.Rating)
                        .FirstOrDefault()),
                StringComparer.Ordinal);

        foreach (var (seriesKey, count) in seasonsWatched)
        {
            // A series with watched seasons but no entry of its own still needs a row
            // here, or its progress would have nowhere to hang.
            context[seriesKey] = context.TryGetValue(seriesKey, out var existing)
                ? existing with { WatchedSeasons = count }
                : UserContext.None with { WatchedSeasons = count };
        }

        return context;
    }

    public TitleCard ToCard(Title title, IReadOnlyDictionary<string, UserContext> context)
    {
        var entry = Context(title.Key, context);

        return new TitleCard(
            title.Key,
            title.MediaType.ToWire(),
            title.TmdbId,
            title.SeasonNumber,
            title.ParentKey,
            title.Name,
            title.ReleaseDate?.Year,
            title.PosterPath,
            title.TmdbVoteAverage,
            title.RuntimeMinutes,
            title.EpisodeCount,
            title.SeasonCount,
            SeasonProgressOf(title, entry),
            title.Genres.Select(g => g.GenreTmdbId).OrderBy(id => id).ToArray(),
            entry.Lists,
            entry.Rating);
    }

    /// <summary>
    /// "3 of 5 seasons watched", or null when there is nothing to say. Null rather
    /// than <c>0 / 5</c> for an untouched series: a zero is not progress, and it
    /// would put an accent on every series card in a grid.
    /// </summary>
    private static SeasonProgressDto? SeasonProgressOf(Title title, UserContext entry)
    {
        if (title.MediaType != MediaType.Series || entry.WatchedSeasons == 0)
        {
            return null;
        }

        // The denominator is TMDB's own count. A series we hold only a summary of
        // has none yet, in which case the seasons we know about are the honest total.
        var total = title.SeasonCount ?? entry.WatchedSeasons;
        return new SeasonProgressDto(entry.WatchedSeasons, Math.Max(total, entry.WatchedSeasons));
    }

    public TitleDetail ToDetail(
        Title title,
        IReadOnlyDictionary<string, UserContext> context,
        bool stale,
        // FR-G4, supplied by be-04's FriendshipService. Defaulted so a caller with no
        // social context (a write path confirming a title exists) stays valid.
        IReadOnlyList<FriendWatchedDto>? friendsWatched = null,
        // Series only; empty for films and seasons.
        IReadOnlyList<SeasonSummaryDto>? seasons = null)
    {
        var entry = Context(title.Key, context);

        return new TitleDetail(
            title.Key,
            title.MediaType.ToWire(),
            title.TmdbId,
            title.SeasonNumber,
            title.ParentKey,
            title.Name,
            title.ReleaseDate?.Year,
            title.PosterPath,
            title.TmdbVoteAverage,
            title.RuntimeMinutes,
            title.EpisodeCount,
            title.SeasonCount,
            SeasonProgressOf(title, entry),
            title.Genres.Select(g => g.GenreTmdbId).OrderBy(id => id).ToArray(),
            entry.Lists,
            entry.Rating,
            title.BackdropPath,
            title.Overview,
            title.ReleaseDate?.ToString("yyyy-MM-dd"),
            title.Genres
                .Where(g => g.Genre is not null)
                .Select(g => new GenreDto(
                    g.GenreTmdbId,
                    g.Genre.Name,
                    g.Genre.MediaTypes().Select(t => t.ToWire()).ToArray()))
                .OrderBy(g => g.Id)
                .ToArray(),
            title.Director,
            TitleCacheService.DeserializeCreators(title.CreatorsJson),
            DeserializeCast(title.CastJson),
            seasons ?? [],
            friendsWatched ?? [],
            stale);
    }

    /// <summary>
    /// The Seasons section of a series' detail, from the season rows the series
    /// fetch already left behind — decorated for the requester in the same one
    /// query the rest of the page uses.
    /// </summary>
    public async Task<IReadOnlyList<SeasonSummaryDto>> LoadSeasonsAsync(
        Guid userId, string seriesKey, CancellationToken ct)
    {
        var rows = await db.Titles
            .AsNoTracking()
            .Where(t => t.ParentKey == seriesKey)
            .OrderBy(t => t.SeasonNumber)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var context = await LoadUserContextAsync(userId, rows.Select(r => r.Key).ToList(), ct);

        return rows
            .Select(r =>
            {
                var entry = Context(r.Key, context);
                return new SeasonSummaryDto(
                    r.Key,
                    r.SeasonNumber ?? 0,
                    r.Name,
                    r.EpisodeCount,
                    r.ReleaseDate?.ToString("yyyy-MM-dd"),
                    r.PosterPath,
                    entry.Lists,
                    entry.Rating);
            })
            .ToArray();
    }

    public static string SerializeCast(IReadOnlyList<CastMemberDto> cast) =>
        JsonSerializer.Serialize(cast, CastJsonOptions);

    public static IReadOnlyList<CastMemberDto> DeserializeCast(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CastMemberDto>>(json, CastJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static UserContext Context(
        string key, IReadOnlyDictionary<string, UserContext> context) =>
        context.TryGetValue(key, out var entry) ? entry : UserContext.None;
}
