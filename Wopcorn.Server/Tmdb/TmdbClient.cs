using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tmdb;

/// <summary>
/// Typed <see cref="HttpClient"/> over the TMDB v3 REST API. Registered as
/// <see cref="ITmdbClient"/>; see Program.cs for the handler configuration.
///
/// Nothing here ever returns a TMDB URL or credential to a caller (FR-B5), and
/// only the request path is logged — never headers, never the query string,
/// which carries <c>api_key</c> when the v3 fallback is in use.
/// </summary>
public sealed class TmdbClient(
    HttpClient http,
    IOptions<TmdbOptions> options,
    IMemoryCache cache,
    ILogger<TmdbClient> logger) : ITmdbClient
{
    private const string MovieGenreCacheKey = "tmdb:genres:movie";
    private const string TvGenreCacheKey = "tmdb:genres:tv";
    private static readonly TimeSpan GenreCacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ProviderDirectoryCacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Process-wide budget (FR-B8): 50 requests/second sustained, 50 burst, with
    /// at most 100 callers waiting. Static because the limit belongs to the
    /// upstream account, not to a DI scope — typed clients are transient.
    /// </summary>
    private static readonly TokenBucketRateLimiter RateLimiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 50,
        TokensPerPeriod = 50,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        AutoReplenishment = true,
    });

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly TmdbOptions _options = options.Value;

    public Task<TmdbPage<TmdbMultiSummary>?> SearchMultiAsync(
        string query, int page, CancellationToken ct) =>
        GetAsync<TmdbPage<TmdbMultiSummary>>(
            "search/multi",
            new Dictionary<string, string?>
            {
                ["query"] = query,
                ["page"] = page.ToString(),
                ["include_adult"] = "false",
                ["language"] = _options.Language,
            },
            ct);

    public Task<TmdbPage<TmdbMovieSummary>?> DiscoverAsync(DiscoverFeed feed, int page, CancellationToken ct) =>
        GetAsync<TmdbPage<TmdbMovieSummary>>(
            feed.ToPath(MediaType.Movie),
            new Dictionary<string, string?>
            {
                ["page"] = page.ToString(),
                ["language"] = _options.Language,
            },
            ct);

    public Task<TmdbPage<TmdbSeriesSummary>?> DiscoverSeriesAsync(
        DiscoverFeed feed, int page, CancellationToken ct) =>
        GetAsync<TmdbPage<TmdbSeriesSummary>>(
            feed.ToPath(MediaType.Series),
            new Dictionary<string, string?>
            {
                ["page"] = page.ToString(),
                ["language"] = _options.Language,
            },
            ct);

    public Task<TmdbMovieDetail?> GetMovieAsync(int id, CancellationToken ct) =>
        GetAsync<TmdbMovieDetail>(
            $"movie/{id}",
            new Dictionary<string, string?>
            {
                ["append_to_response"] = "credits",
                ["language"] = _options.Language,
            },
            ct);

    public Task<TmdbSeriesDetail?> GetSeriesAsync(int id, CancellationToken ct) =>
        GetAsync<TmdbSeriesDetail>(
            $"tv/{id}",
            new Dictionary<string, string?>
            {
                // aggregate_credits would be the fuller answer for a series, but it
                // is a different shape; `credits` keeps one cast projection for all
                // three media types.
                ["append_to_response"] = "credits",
                ["language"] = _options.Language,
            },
            ct);

    /// <summary>
    /// A season is addressed by its series and number — a TMDB season's own
    /// <c>id</c> is not routable, which is why the title key carries both.
    /// </summary>
    public Task<TmdbSeasonDetail?> GetSeasonAsync(
        int seriesId, int seasonNumber, CancellationToken ct) =>
        GetAsync<TmdbSeasonDetail>(
            $"tv/{seriesId}/season/{seasonNumber}",
            new Dictionary<string, string?>
            {
                ["append_to_response"] = "credits",
                ["language"] = _options.Language,
            },
            ct);

    public Task<IReadOnlyList<TmdbGenre>> GetGenresAsync(CancellationToken ct) =>
        GenreListAsync(MovieGenreCacheKey, "genre/movie/list", ct);

    public Task<IReadOnlyList<TmdbGenre>> GetTvGenresAsync(CancellationToken ct) =>
        GenreListAsync(TvGenreCacheKey, "genre/tv/list", ct);

    /// <summary>
    /// Providers for one title. The whole world comes back in one response and all
    /// of it is deserialised — <c>AvailabilityService</c> stores every region,
    /// because a second user in a second region would otherwise re-fetch a payload
    /// already in hand.
    /// </summary>
    public Task<TmdbWatchProviders?> GetWatchProvidersAsync(
        MediaType mediaType, int tmdbId, CancellationToken ct) =>
        GetAsync<TmdbWatchProviders>(
            $"{ProvidersSegment(mediaType)}/{tmdbId}/watch/providers",
            // Deliberately no `language`: this payload is ids, names and logo paths,
            // none of which TMDB localises.
            new Dictionary<string, string?>(),
            ct);

    public async Task<IReadOnlyList<TmdbProviderDirectoryEntry>> GetProviderDirectoryAsync(
        MediaType mediaType, string region, CancellationToken ct)
    {
        var side = ProvidersSegment(mediaType);
        var cacheKey = $"tmdb:providers:{side}:{region}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<TmdbProviderDirectoryEntry>? cached)
            && cached is not null)
        {
            return cached;
        }

        var response = await GetAsync<TmdbProviderDirectory>(
            $"watch/providers/{side}",
            new Dictionary<string, string?> { ["watch_region"] = region },
            ct);

        var entries = response?.Results?
                          .Where(p => !string.IsNullOrWhiteSpace(p.ProviderName))
                          .ToList()
                      ?? (IReadOnlyList<TmdbProviderDirectoryEntry>)[];

        if (entries.Count > 0)
        {
            // The directory changes monthly at most; three screens read it.
            cache.Set(cacheKey, entries, ProviderDirectoryCacheTtl);
        }

        return entries;
    }

    /// <summary>
    /// The upstream path segment for a media type. Seasons have no providers
    /// endpoint at all, so this throws rather than guessing.
    /// </summary>
    private static string ProvidersSegment(MediaType mediaType) => mediaType switch
    {
        MediaType.Movie => "movie",
        MediaType.Series => "tv",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType),
            "TMDB exposes watch providers for films and series only; resolve a season to its series first."),
    };

    private async Task<IReadOnlyList<TmdbGenre>> GenreListAsync(
        string cacheKey, string path, CancellationToken ct)
    {
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<TmdbGenre>? cached) && cached is not null)
        {
            return cached;
        }

        var response = await GetAsync<TmdbGenreList>(
            path,
            new Dictionary<string, string?> { ["language"] = _options.Language },
            ct);

        var genres = response?.Genres?.Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList()
            ?? (IReadOnlyList<TmdbGenre>)[];

        if (genres.Count > 0)
        {
            cache.Set(cacheKey, genres, GenreCacheTtl);
        }

        return genres;
    }

    private async Task<T?> GetAsync<T>(string path, Dictionary<string, string?> query, CancellationToken ct)
        where T : class
    {
        var hasBearer = !string.IsNullOrWhiteSpace(_options.ReadAccessToken);
        if (!hasBearer)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                // No credentials at all. Program.cs already logged the startup
                // warning; every catalog call degrades to 503.
                throw new TmdbUnavailableException("TMDB is not configured.");
            }

            query["api_key"] = _options.ApiKey;
        }

        var uri = BuildUri(path, query);

        using var response = await SendAsync(uri, path, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TMDB {Path} answered {Status}.", path, (int)response.StatusCode);
            throw new TmdbUnavailableException("TMDB returned an error response.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "TMDB {Path} returned a body that could not be parsed.", path);
            throw new TmdbUnavailableException("TMDB returned an unreadable response.", ex);
        }
    }

    /// <summary>
    /// One request, plus a single retry when TMDB answers 429 with a
    /// <c>Retry-After</c>. Everything that is not an HTTP answer becomes
    /// <see cref="TmdbUnavailableException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string uri, string path, CancellationToken ct)
    {
        var response = await SendOnceAsync(uri, path, ct);

        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return response;
        }

        var retryAfter = RetryAfter(response);
        response.Dispose();
        logger.LogWarning("TMDB {Path} was rate limited; retrying in {Seconds}s.", path, retryAfter.TotalSeconds);

        await Task.Delay(retryAfter, ct);

        response = await SendOnceAsync(uri, path, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            response.Dispose();
            throw new TmdbUnavailableException("TMDB is rate limiting this instance.");
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(string uri, string path, CancellationToken ct)
    {
        using var lease = await RateLimiter.AcquireAsync(permitCount: 1, ct);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("TMDB {Path} dropped: the local rate-limit queue is full.", path);
            throw new TmdbUnavailableException("Too many TMDB requests are already in flight.");
        }

        try
        {
            return await http.GetAsync(uri, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout, not the caller giving up.
            logger.LogWarning("TMDB {Path} timed out.", path);
            throw new TmdbUnavailableException("TMDB timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("TMDB {Path} could not be reached.", path);
            throw new TmdbUnavailableException("TMDB could not be reached.", ex);
        }
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
        if (seconds is null && response.Headers.RetryAfter?.Date is { } date)
        {
            seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
        }

        // Clamped: a hostile or absent header must not park a request thread.
        return TimeSpan.FromSeconds(Math.Clamp(seconds ?? 1, 1, 10));
    }

    private static string BuildUri(string path, Dictionary<string, string?> query)
    {
        var pairs = query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");

        return $"{path}?{string.Join('&', pairs)}";
    }
}
