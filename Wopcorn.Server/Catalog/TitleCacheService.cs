using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Catalog;

/// <summary>
/// The outcome of <see cref="TitleCacheService.GetDetailAsync"/>. "TMDB says this
/// title does not exist" and "TMDB is unreachable and we have nothing cached" are
/// different answers — 404 and 503 — and a nullable tuple cannot carry that.
/// </summary>
/// <remarks>
/// Title != null, Stale = false  → fresh (within TTL, or just fetched)<br/>
/// Title != null, Stale = true   → upstream did not refresh; cached copy served<br/>
/// Title == null, NotFound = true  → TMDB returned 404 and nothing is cached<br/>
/// Title == null, NotFound = false → upstream unreachable and nothing is cached
/// </remarks>
public record TitleDetailResult(Title? Title, bool Stale, bool NotFound);

/// <summary>
/// The only type that writes to the <c>Titles</c> table, and the only caller of
/// <see cref="ITmdbClient"/> for catalog data (FR-B6, FR-B7, NFR-2).
///
/// Films, series and seasons all pass through here. The TTL and staleness
/// contract is one rule for all three, and so is the summary/detail split: a
/// search or discovery page writes summary columns, and only opening a title
/// fetches its detail.
/// </summary>
public sealed class TitleCacheService(
    WopcornDbContext db,
    ITmdbClient tmdb,
    GenreCatalogService genres)
{
    /// <summary>How long a search/discover summary row stays authoritative.</summary>
    public static readonly TimeSpan SummaryTtl = TimeSpan.FromDays(14);

    /// <summary>How long a full detail row stays authoritative.</summary>
    public static readonly TimeSpan DetailTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions CreatorsJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>"This payload said nothing about genres" — never "it has none".</summary>
    private static readonly IReadOnlyDictionary<int, Genre> NoGenres = new Dictionary<int, Genre>();

    /// <summary>Pure staleness policy, exposed so the TTL boundary is unit-testable.</summary>
    public static bool IsSummaryFresh(DateTimeOffset fetchedAt, DateTimeOffset now) =>
        now - fetchedAt < SummaryTtl;

    /// <inheritdoc cref="IsSummaryFresh"/>
    public static bool IsDetailFresh(DateTimeOffset? fetchedAt, DateTimeOffset now) =>
        fetchedAt is { } at && now - at < DetailTtl;

    // ------------------------------------------------------------- summaries

    /// <summary>
    /// Called after every search/discover response. Inserts unknown titles and
    /// refreshes the summary columns of rows whose summary has aged past
    /// <see cref="SummaryTtl"/>, syncing <c>TitleGenre</c> from <c>genre_ids</c>.
    /// One round trip to load, one <c>SaveChanges</c> to write, whatever the mix of
    /// media types on the page.
    /// </summary>
    public async Task<IReadOnlyList<Title>> UpsertSummariesAsync(
        IEnumerable<TitleSummary> incoming, CancellationToken ct)
    {
        var summaries = incoming
            .Where(s => s.Key.TmdbId > 0)
            .GroupBy(s => s.Key.Value, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (summaries.Count == 0)
        {
            return [];
        }

        // Before any title rows are added: the catalog write inside this call
        // performs its own SaveChanges.
        var knownGenreIds = await genres.KnownIdsAsync(ct);

        var keys = summaries.Select(s => s.Key.Value).ToList();
        var existing = await db.Titles
            .Include(t => t.Genres)
            .Where(t => keys.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, StringComparer.Ordinal, ct);

        var now = DateTimeOffset.UtcNow;
        var result = new List<Title>(summaries.Count);

        foreach (var summary in summaries)
        {
            if (!existing.TryGetValue(summary.Key.Value, out var title))
            {
                title = Title.New(summary.Key, NameOf(summary.Name, summary.OriginalName, null), now);
                db.Titles.Add(title);
                existing[title.Key] = title;

                ApplySummary(title, summary, now);
                SyncGenres(title, summary.GenreIds, knownGenreIds);
            }
            else if (!IsSummaryFresh(title.SummaryFetchedAt, now))
            {
                ApplySummary(title, summary, now);
                SyncGenres(title, summary.GenreIds, knownGenreIds);
            }

            result.Add(title);
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// Pure database read — never upstream. This is what list, queue and feed
    /// rendering use: 200 titles is one query (NFR-2, FR-B6).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, Title>> GetManyAsync(
        IEnumerable<string> keys, CancellationToken ct)
    {
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<string, Title>(StringComparer.Ordinal);
        }

        return await db.Titles
            .AsNoTracking()
            .Include(t => t.Genres)
            .Where(t => wanted.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, StringComparer.Ordinal, ct);
    }

    // ---------------------------------------------------------------- detail

    /// <summary>
    /// Serves the cached row while <c>DetailFetchedAt</c> is within
    /// <see cref="DetailTtl"/>, otherwise refreshes from TMDB. An upstream failure
    /// never takes down a detail view that has something to show (FR-B8, NFR-10).
    ///
    /// Dispatches on the key's media type — the three upstream endpoints have
    /// nothing in common but this contract.
    /// </summary>
    public async Task<TitleDetailResult> GetDetailAsync(
        TitleKey key, bool forceRefresh, CancellationToken ct)
    {
        var title = await db.Titles
            .Include(t => t.Genres)
            .ThenInclude(tg => tg.Genre)
            .FirstOrDefaultAsync(t => t.Key == key.Value, ct);

        var now = DateTimeOffset.UtcNow;
        if (title is not null && !forceRefresh && IsDetailFresh(title.DetailFetchedAt, now))
        {
            return new TitleDetailResult(title, Stale: false, NotFound: false);
        }

        return key.MediaType switch
        {
            MediaType.Movie => await MovieDetailAsync(key, title, now, ct),
            MediaType.Series => await SeriesDetailAsync(key, title, now, ct),
            MediaType.Season => await SeasonDetailAsync(key, title, now, ct),
            _ => new TitleDetailResult(null, Stale: false, NotFound: true),
        };
    }

    private async Task<TitleDetailResult> MovieDetailAsync(
        TitleKey key, Title? title, DateTimeOffset now, CancellationToken ct)
    {
        TmdbMovieDetail? detail;
        try
        {
            detail = await tmdb.GetMovieAsync(key.TmdbId, ct);
        }
        catch (TmdbUnavailableException)
        {
            // Cached copy beats an error page; no cached copy becomes 503.
            return new TitleDetailResult(title, Stale: true, NotFound: false);
        }

        if (detail is null)
        {
            return Missing(title);
        }

        var genreRows = await genres.EnsureAsync(detail.Genres ?? [], MediaType.Movie, ct);

        title ??= Add(key, NameOf(detail.Title, detail.OriginalTitle, null), now);

        ApplyCommonDetail(
            title,
            NameOf(detail.Title, detail.OriginalTitle, title.Name),
            detail.ReleaseDate,
            detail.PosterPath,
            detail.BackdropPath,
            detail.VoteAverage,
            detail.Overview,
            genreRows,
            now);

        title.RuntimeMinutes = detail.Runtime is > 0 ? detail.Runtime : null;
        title.Director = detail.Credits?.Crew?
            .FirstOrDefault(c => string.Equals(c.Job, "Director", StringComparison.Ordinal))?.Name;
        title.CastJson = TitleMapper.SerializeCast(CastOf(detail.Credits));

        await db.SaveChangesAsync(ct);
        return new TitleDetailResult(title, Stale: false, NotFound: false);
    }

    /// <summary>
    /// A series detail also writes a <b>summary row per season</b>, from the
    /// <c>seasons[]</c> array TMDB already returns. That is what makes opening a
    /// series cost one upstream request rather than one per season, and it is what
    /// gives the season toggles rows to point at before anyone opens one.
    /// </summary>
    private async Task<TitleDetailResult> SeriesDetailAsync(
        TitleKey key, Title? title, DateTimeOffset now, CancellationToken ct)
    {
        TmdbSeriesDetail? detail;
        try
        {
            detail = await tmdb.GetSeriesAsync(key.TmdbId, ct);
        }
        catch (TmdbUnavailableException)
        {
            return new TitleDetailResult(title, Stale: true, NotFound: false);
        }

        if (detail is null)
        {
            return Missing(title);
        }

        var genreRows = await genres.EnsureAsync(detail.Genres ?? [], MediaType.Series, ct);

        title ??= Add(key, NameOf(detail.Name, detail.OriginalName, null), now);

        ApplyCommonDetail(
            title,
            NameOf(detail.Name, detail.OriginalName, title.Name),
            detail.FirstAirDate,
            detail.PosterPath,
            detail.BackdropPath,
            detail.VoteAverage,
            detail.Overview,
            genreRows,
            now);

        title.EpisodeCount = detail.NumberOfEpisodes is > 0 ? detail.NumberOfEpisodes : null;
        title.SeasonCount = detail.NumberOfSeasons is > 0 ? detail.NumberOfSeasons : null;
        title.RuntimeMinutes = TmdbRuntime.ForEpisodes(detail.EpisodeRunTime, title.EpisodeCount);
        title.Director = null;
        title.CreatorsJson = SerializeCreators(detail.CreatedBy);
        title.CastJson = TitleMapper.SerializeCast(CastOf(detail.Credits));

        await UpsertSeasonRowsAsync(key, detail, genreRows, now, ct);

        await db.SaveChangesAsync(ct);
        return new TitleDetailResult(title, Stale: false, NotFound: false);
    }

    private async Task<TitleDetailResult> SeasonDetailAsync(
        TitleKey key, Title? title, DateTimeOffset now, CancellationToken ct)
    {
        TmdbSeasonDetail? detail;
        try
        {
            detail = await tmdb.GetSeasonAsync(key.TmdbId, key.SeasonNumber ?? 0, ct);
        }
        catch (TmdbUnavailableException)
        {
            return new TitleDetailResult(title, Stale: true, NotFound: false);
        }

        if (detail is null)
        {
            return Missing(title);
        }

        // A season may never exist without its series row: its ParentKey is a
        // foreign key, and the two are written in the same SaveChanges.
        var series = await EnsureSeriesRowAsync(key, now, ct);
        if (series is null)
        {
            // The series is unreachable and unknown, so there is nothing to hang the
            // season off. Not a 404 — the season may well exist.
            return new TitleDetailResult(title, Stale: true, NotFound: false);
        }

        // Creating the series also writes a summary row for every season on its
        // `seasons[]` array — including this one. So the row may exist now even
        // though it did not when this method was entered, and adding a second
        // instance with the same key is something EF refuses to track.
        title ??= db.Titles.Local.FirstOrDefault(t => t.Key == key.Value)
            ?? Add(key, NameOf(detail.Name, null, null), now);

        var episodes = detail.Episodes ?? [];

        ApplyCommonDetail(
            title,
            NameOf(detail.Name, null, title.Name),
            detail.AirDate,
            detail.PosterPath,
            // TMDB season objects carry no backdrop; the series' one is the series'.
            backdropPath: null,
            detail.VoteAverage,
            detail.Overview,
            // A season's genres are its series' genres — TMDB season objects carry
            // none of their own, so nothing here syncs them and CopyGenresFromSeries
            // below does the whole job.
            genreRows: NoGenres,
            now);

        title.EpisodeCount = episodes.Count > 0 ? episodes.Count : null;
        title.SeasonCount = null;
        title.RuntimeMinutes = TmdbRuntime.ForEpisodes(SeriesRunTime(series), title.EpisodeCount);
        title.CastJson = TitleMapper.SerializeCast(CastOf(detail.Credits));

        CopyGenresFromSeries(title, series);

        await db.SaveChangesAsync(ct);
        return new TitleDetailResult(title, Stale: false, NotFound: false);
    }

    // ------------------------------------------------------ detail internals

    /// <summary>
    /// TMDB 404. With nothing cached that is a genuine 404; with a cached row we
    /// serve it, flagged, because the refresh did not land.
    /// </summary>
    private static TitleDetailResult Missing(Title? title) =>
        title is null
            ? new TitleDetailResult(null, Stale: false, NotFound: true)
            : new TitleDetailResult(title, Stale: true, NotFound: false);

    private Title Add(TitleKey key, string name, DateTimeOffset now)
    {
        var title = Title.New(key, name, now);
        db.Titles.Add(title);
        return title;
    }

    /// <summary>
    /// Loads or creates the series a season belongs to. A summary row is enough —
    /// the season screen only needs the parent to exist and to carry the genres the
    /// season inherits; opening the series itself is what fills the rest in.
    /// </summary>
    private async Task<Title?> EnsureSeriesRowAsync(
        TitleKey seasonKey, DateTimeOffset now, CancellationToken ct)
    {
        var seriesKey = TitleKey.ForSeries(seasonKey.TmdbId);

        var series = await db.Titles
            .Include(t => t.Genres)
            .FirstOrDefaultAsync(t => t.Key == seriesKey.Value, ct);

        if (series is not null)
        {
            return series;
        }

        TmdbSeriesDetail? detail;
        try
        {
            detail = await tmdb.GetSeriesAsync(seriesKey.TmdbId, ct);
        }
        catch (TmdbUnavailableException)
        {
            return null;
        }

        if (detail is null)
        {
            return null;
        }

        var genreRows = await genres.EnsureAsync(detail.Genres ?? [], MediaType.Series, ct);

        series = Add(seriesKey, NameOf(detail.Name, detail.OriginalName, null), now);

        ApplyCommonDetail(
            series,
            NameOf(detail.Name, detail.OriginalName, series.Name),
            detail.FirstAirDate,
            detail.PosterPath,
            detail.BackdropPath,
            detail.VoteAverage,
            detail.Overview,
            genreRows,
            now);

        series.EpisodeCount = detail.NumberOfEpisodes is > 0 ? detail.NumberOfEpisodes : null;
        series.SeasonCount = detail.NumberOfSeasons is > 0 ? detail.NumberOfSeasons : null;
        series.RuntimeMinutes = TmdbRuntime.ForEpisodes(detail.EpisodeRunTime, series.EpisodeCount);
        series.CreatorsJson = SerializeCreators(detail.CreatedBy);
        series.CastJson = TitleMapper.SerializeCast(CastOf(detail.Credits));

        await UpsertSeasonRowsAsync(seriesKey, detail, genreRows, now, ct);

        return series;
    }

    /// <summary>
    /// Writes one summary row per season from the series payload. Season rows are
    /// never marked detail-fetched here — only opening a season does that, which is
    /// what keeps its <c>stale</c> flag and its own TTL honest.
    /// </summary>
    private async Task UpsertSeasonRowsAsync(
        TitleKey seriesKey,
        TmdbSeriesDetail detail,
        IReadOnlyDictionary<int, Genre> genreRows,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var seasons = detail.Seasons ?? [];
        if (seasons.Count == 0)
        {
            return;
        }

        var keys = seasons
            .Select(s => TitleKey.ForSeason(seriesKey.TmdbId, s.SeasonNumber).Value)
            .ToList();

        var existing = await db.Titles
            .Include(t => t.Genres)
            .Where(t => keys.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, StringComparer.Ordinal, ct);

        foreach (var season in seasons)
        {
            var key = TitleKey.ForSeason(seriesKey.TmdbId, season.SeasonNumber);
            var name = NameOf(season.Name, null, $"Season {season.SeasonNumber}");

            if (!existing.TryGetValue(key.Value, out var row))
            {
                row = Add(key, name, now);
            }
            else if (IsSummaryFresh(row.SummaryFetchedAt, now))
            {
                continue;
            }

            row.Name = name;
            row.ReleaseDate = ParseDate(season.AirDate) ?? row.ReleaseDate;
            row.PosterPath = season.PosterPath ?? row.PosterPath;
            row.EpisodeCount = season.EpisodeCount is > 0 ? season.EpisodeCount : row.EpisodeCount;
            row.RuntimeMinutes = TmdbRuntime.ForEpisodes(detail.EpisodeRunTime, row.EpisodeCount);
            if (!string.IsNullOrWhiteSpace(season.Overview))
            {
                row.Overview = season.Overview;
            }

            row.SummaryFetchedAt = now;

            SyncGenres(row, genreRows.Keys, [.. genreRows.Keys]);
        }
    }

    /// <summary>
    /// Everything a detail payload sets regardless of media type. Extracted because
    /// three call sites setting eight fields each is three chances to forget one.
    /// </summary>
    private void ApplyCommonDetail(
        Title title,
        string name,
        string? releaseDate,
        string? posterPath,
        string? backdropPath,
        double? voteAverage,
        string? overview,
        IReadOnlyDictionary<int, Genre> genreRows,
        DateTimeOffset now)
    {
        title.Name = name;
        title.ReleaseDate = ParseDate(releaseDate) ?? title.ReleaseDate;
        title.PosterPath = posterPath ?? title.PosterPath;
        title.BackdropPath = backdropPath ?? title.BackdropPath;
        title.TmdbVoteAverage = voteAverage ?? title.TmdbVoteAverage;
        title.Overview = overview;

        // An empty `genres` array on the detail payload is treated as "no news", not
        // as "this title has no genres" — never wipe what the summary knew.
        if (genreRows.Count > 0)
        {
            SyncGenres(title, genreRows.Keys, [.. genreRows.Keys]);

            foreach (var link in title.Genres)
            {
                if (genreRows.TryGetValue(link.GenreTmdbId, out var genre))
                {
                    link.Genre = genre;
                }
            }
        }

        title.SummaryFetchedAt = now;
        title.DetailFetchedAt = now;
    }

    private static void ApplySummary(Title title, TitleSummary summary, DateTimeOffset now)
    {
        title.Name = NameOf(summary.Name, summary.OriginalName, title.Name);
        title.ReleaseDate = ParseDate(summary.ReleaseDate) ?? title.ReleaseDate;
        title.PosterPath = summary.PosterPath ?? title.PosterPath;
        title.BackdropPath = summary.BackdropPath ?? title.BackdropPath;
        title.TmdbVoteAverage = summary.VoteAverage ?? title.TmdbVoteAverage;
        // Summaries carry an overview but never runtime, cast, episode or season
        // counts — those stay whatever the detail fetch last wrote.
        if (!string.IsNullOrWhiteSpace(summary.Overview))
        {
            title.Overview = summary.Overview;
        }

        title.SummaryFetchedAt = now;
    }

    /// <summary>A season inherits its series' genre links verbatim.</summary>
    private void CopyGenresFromSeries(Title season, Title series)
    {
        var desired = series.Genres.Select(g => g.GenreTmdbId).ToHashSet();
        SyncGenres(season, desired, desired);
    }

    /// <summary>
    /// Reconciles the join rows against the ids TMDB reported, dropping any id the
    /// <c>Genres</c> table does not carry — a join row pointing at an unknown genre
    /// would violate the foreign key.
    /// </summary>
    private void SyncGenres(Title title, IEnumerable<int>? incomingIds, HashSet<int> knownGenreIds)
    {
        var desired = (incomingIds ?? []).Where(knownGenreIds.Contains).ToHashSet();

        var stale = title.Genres.Where(g => !desired.Contains(g.GenreTmdbId)).ToList();
        foreach (var link in stale)
        {
            title.Genres.Remove(link);
            db.Set<TitleGenre>().Remove(link);
        }

        foreach (var id in desired.Where(id => title.Genres.All(g => g.GenreTmdbId != id)))
        {
            title.Genres.Add(new TitleGenre { TitleKey = title.Key, GenreTmdbId = id });
        }
    }

    private static IReadOnlyList<CastMemberDto> CastOf(TmdbCredits? credits) =>
        (credits?.Cast ?? [])
        .OrderBy(c => c.Order)
        .Where(c => !string.IsNullOrWhiteSpace(c.Name))
        .Take(12)
        .Select(c => new CastMemberDto(c.Name!, c.Character, c.ProfilePath))
        .ToArray();

    private static string? SerializeCreators(IReadOnlyList<TmdbCreator>? createdBy)
    {
        var names = (createdBy ?? [])
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToArray();

        return names.Length == 0 ? null : JsonSerializer.Serialize(names, CreatorsJsonOptions);
    }

    public static IReadOnlyList<string> DeserializeCreators(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, CreatorsJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// A stored series row does not keep <c>episode_run_time</c>, only the product.
    /// Dividing it back out is how a season derives its own runtime without a
    /// second series fetch, and it degrades to null exactly where the array was
    /// empty — which is the answer we want anyway.
    /// </summary>
    private static IReadOnlyList<int>? SeriesRunTime(Title series) =>
        series is { RuntimeMinutes: > 0, EpisodeCount: > 0 }
            ? [series.RuntimeMinutes.Value / series.EpisodeCount.Value]
            : null;

    private static string NameOf(string? name, string? originalName, string? fallback) =>
        !string.IsNullOrWhiteSpace(name) ? name
        : !string.IsNullOrWhiteSpace(originalName) ? originalName
        : fallback ?? "Untitled";

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
}

/// <summary>
/// One row of a search or discovery page, normalised across media types. TMDB
/// spells a series' fields differently from a film's, and the cache should not
/// have to care which endpoint a page came from.
/// </summary>
public record TitleSummary(
    TitleKey Key,
    string? Name,
    string? OriginalName,
    string? Overview,
    string? PosterPath,
    string? BackdropPath,
    string? ReleaseDate,
    double? VoteAverage,
    IReadOnlyList<int>? GenreIds)
{
    public static TitleSummary From(TmdbMovieSummary movie) =>
        new(TitleKey.ForMovie(movie.Id), movie.Title, movie.OriginalTitle, movie.Overview,
            movie.PosterPath, movie.BackdropPath, movie.ReleaseDate, movie.VoteAverage,
            movie.GenreIds);

    public static TitleSummary From(TmdbSeriesSummary series) =>
        new(TitleKey.ForSeries(series.Id), series.Name, series.OriginalName, series.Overview,
            series.PosterPath, series.BackdropPath, series.FirstAirDate, series.VoteAverage,
            series.GenreIds);
}
