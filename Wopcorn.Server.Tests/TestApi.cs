using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>Shapes the API answers back, so tests read like the contract.</summary>
public record UserSummaryDto(string Id, string DisplayName, string? AvatarUrl);

public record ApiErrorDto(string Code, string Message, Dictionary<string, string[]>? Errors);

// API-CONTRACT.md "Lists", "Queue ordering" and "Ratings" — declared from the
// contract rather than imported from the server, so a server-side rename that
// changes the wire shape fails these tests instead of silently following along.

public record ListsDto(bool Watched, bool Watchlist, bool Queue);

public record SeasonProgressDto(int Watched, int Total);

public record CardDto(
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
    int[] GenreIds,
    ListsDto Lists,
    int? MyRating,
    int[] AvailableOn);

public record EntryDto(
    CardDto Title, DateTimeOffset AddedAt, int? Position, string? WatchedOn, int? Rating);

public record ListPageDto(int Count, EntryDto[] Entries);

// API-CONTRACT.md "Friends" and "Feed" (be-04).

public record TasteMatchDto(int? Score, int SharedCount, bool Qualified);

public record FriendDto(UserSummaryDto User, DateTimeOffset FriendsSince, TasteMatchDto TasteMatch);

public record FriendRequestDto(string Id, UserSummaryDto User, DateTimeOffset SentAt);

public record FriendsResponseDto(
    FriendDto[] Friends, FriendRequestDto[] Incoming, FriendRequestDto[] Outgoing);

public record UserSearchResultDto(
    string Id, string DisplayName, string? AvatarUrl, string Relationship);

public record ListCountsDto(int Watched, int Watchlist, int Queue);

/// <summary>
/// The subset of the profile payload the be-04 suites assert on. Kept narrow on
/// purpose: those tests are about visibility and taste match, and deserializing
/// into fewer members than the wire carries is exactly what lets the profile grow
/// without dragging them along.
/// </summary>
public record FriendProfileDto(
    UserSummaryDto User, RatingStatsDto Stats, ListCountsDto Counts, TasteMatchDto TasteMatch);

// API-CONTRACT.md "Profile and favourites".

public record GenreAffinityDto(int Id, string Name, int Count);

public record RuntimeOnRecordDto(int Minutes, int KnownTitles, int UnknownTitles);

public record ProfileDto(
    UserSummaryDto User,
    bool IsSelf,
    DateTimeOffset MemberSince,
    RatingStatsDto Stats,
    ListCountsDto Counts,
    CardDto[] Favorites,
    GenreAffinityDto[] TopGenres,
    RuntimeOnRecordDto Runtime,
    int FriendCount,
    ActivityItemDto[] RecentActivity,
    TasteMatchDto? TasteMatch);

public record ActivityItemDto(
    string Id, UserSummaryDto User, string Kind, CardDto Title, int? Rating,
    DateTimeOffset OccurredAt);

public record FeedPageDto(ActivityItemDto[] Items, string? NextCursor);

public record FriendWatchedDto(UserSummaryDto User, int? Rating);

public record SeasonSummaryDto(
    string Key, int SeasonNumber, string Name, int? EpisodeCount, string? AirDate,
    string? PosterPath, ListsDto Lists, int? MyRating);

public record TitleDetailDto(
    string Key,
    string MediaType,
    int TmdbId,
    int? SeasonNumber,
    string? ParentKey,
    string Title,
    int[] GenreIds,
    int? RuntimeMinutes,
    int? EpisodeCount,
    int? SeasonCount,
    SeasonProgressDto? SeasonProgress,
    string? Director,
    string[] Creators,
    SeasonSummaryDto[] Seasons,
    int? MyRating,
    FriendWatchedDto[] FriendsWatched,
    bool Stale);

public record TitlePageDto(int Page, int TotalPages, int TotalResults, CardDto[] Results);

public record GenreDto(int Id, string Name, string[] MediaTypes);

public record QueueOrderDto(string[] Keys);

public record QueueOutOfSyncDto(string Code, string Message, string[] Keys);

public record RatingStatsDto(int Count, double? Average, int[] Distribution);

// API-CONTRACT.md "Streaming availability" (plan 09).

public record WatchProviderDto(int Id, string Name, string? LogoPath);

public record OfferGroupDto(string Kind, WatchProviderDto[] Providers);

public record AvailabilityDto(
    string Region, DateTimeOffset? FetchedAt, string? Link, OfferGroupDto[] Offers);

public record ServicesDto(string Region, int[] ProviderIds);

public record MeDto(
    string Id, string DisplayName, string? AvatarUrl, string? Region, int[] ProviderIds);

public static class TestApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string password, string displayName) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static async Task<UserSummaryDto> RegisterAndReadAsync(
        this HttpClient client, string email, string password, string displayName)
    {
        var response = await client.RegisterAsync(email, password, displayName);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<UserSummaryDto>();
    }

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(body, Json);
        Assert.NotNull(value);
        return value;
    }

    /// <summary>
    /// Asserts the response body is the contract's ApiError object — not an HTML
    /// login page, not an empty body, not a ProblemDetails.
    /// </summary>
    public static async Task<ApiErrorDto> ReadApiErrorAsync(this HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", mediaType);

        var error = await response.ReadAsAsync<ApiErrorDto>();
        Assert.False(string.IsNullOrWhiteSpace(error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    // --- lists, queue and ratings (be-03) -----------------------------------

    /// <summary>The title key for a film, spelled the way the API spells it.</summary>
    public static string Movie(int tmdbId) => $"movie-{tmdbId}";

    /// <summary>The title key for a series.</summary>
    public static string Series(int tmdbId) => $"tv-{tmdbId}";

    /// <summary>The title key for one season of a series.</summary>
    public static string Season(int tmdbId, int seasonNumber) => $"tv-{tmdbId}-s{seasonNumber}";

    /// <summary>
    /// The bodyless form the contract calls for:
    /// <c>PUT /api/lists/watchlist/movie-438631</c> with no content type at all.
    /// </summary>
    public static Task<HttpResponseMessage> AddToListAsync(
        this HttpClient client, string list, string key) =>
        client.PutAsync($"/api/lists/{list}/{key}", null);

    public static Task<HttpResponseMessage> AddToListAsync(
        this HttpClient client, string list, string key, object body) =>
        client.PutAsJsonAsync($"/api/lists/{list}/{key}", body);

    public static Task<HttpResponseMessage> RemoveFromListAsync(
        this HttpClient client, string list, string key) =>
        client.DeleteAsync($"/api/lists/{list}/{key}");

    public static Task<HttpResponseMessage> RateAsync(
        this HttpClient client, string key, int rating) =>
        client.PutAsJsonAsync($"/api/titles/{key}/rating", new { rating });

    public static Task<HttpResponseMessage> ClearRatingAsync(this HttpClient client, string key) =>
        client.DeleteAsync($"/api/titles/{key}/rating");

    public static Task<HttpResponseMessage> ReorderQueueAsync(
        this HttpClient client, params string[] keys) =>
        client.PutAsJsonAsync("/api/queue/order", new { keys });

    public static Task<HttpResponseMessage> SortQueueAsync(
        this HttpClient client, string preset, string? dir = null) =>
        client.PostAsJsonAsync("/api/queue/sort", new { preset, dir });

    /// <summary>Adds a title and asserts it landed, returning the entry.</summary>
    public static async Task<EntryDto> AddAndReadAsync(
        this HttpClient client, string list, string key)
    {
        var response = await client.AddToListAsync(list, key);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<EntryDto>();
    }

    // Film-id overloads. Most of these suites predate series and are about
    // behaviour that films and series share — ordering, idempotency, queue
    // mechanics — so they stay readable by naming a film by its TMDB id and
    // letting `Movie` spell the key. Anything actually *about* media types spells
    // the key out.

    public static Task<HttpResponseMessage> AddToListAsync(
        this HttpClient client, string list, int tmdbId) =>
        client.AddToListAsync(list, Movie(tmdbId));

    public static Task<HttpResponseMessage> AddToListAsync(
        this HttpClient client, string list, int tmdbId, object body) =>
        client.AddToListAsync(list, Movie(tmdbId), body);

    public static Task<HttpResponseMessage> RemoveFromListAsync(
        this HttpClient client, string list, int tmdbId) =>
        client.RemoveFromListAsync(list, Movie(tmdbId));

    public static Task<HttpResponseMessage> RateAsync(
        this HttpClient client, int tmdbId, int rating) =>
        client.RateAsync(Movie(tmdbId), rating);

    public static Task<HttpResponseMessage> ClearRatingAsync(this HttpClient client, int tmdbId) =>
        client.ClearRatingAsync(Movie(tmdbId));

    public static Task<HttpResponseMessage> ReorderQueueAsync(
        this HttpClient client, params int[] tmdbIds) =>
        client.ReorderQueueAsync([.. tmdbIds.Select(Movie)]);

    public static Task<EntryDto> AddAndReadAsync(
        this HttpClient client, string list, int tmdbId) =>
        client.AddAndReadAsync(list, Movie(tmdbId));

    /// <summary>Fetches a title's detail and asserts it came back.</summary>
    public static async Task<TitleDetailDto> GetTitleAsync(this HttpClient client, string key)
    {
        var response = await client.GetAsync($"/api/titles/{key}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<TitleDetailDto>();
    }

    public static async Task<TitlePageDto> SearchTitlesAsync(
        this HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/titles/search?q={Uri.EscapeDataString(query)}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<TitlePageDto>();
    }

    public static async Task<ListPageDto> GetListAsync(
        this HttpClient client, string list, string query = "")
    {
        var response = await client.GetAsync($"/api/lists/{list}{query}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<ListPageDto>();
    }

    // --- friends and feed (be-04) -------------------------------------------

    public static Task<HttpResponseMessage> SendFriendRequestAsync(
        this HttpClient client, Guid userId) =>
        client.PostAsJsonAsync("/api/friends/requests", new { userId });

    public static Task<HttpResponseMessage> AcceptFriendRequestAsync(
        this HttpClient client, string requestId) =>
        client.PostAsync($"/api/friends/requests/{requestId}/accept", null);

    public static Task<HttpResponseMessage> DeclineFriendRequestAsync(
        this HttpClient client, string requestId) =>
        client.PostAsync($"/api/friends/requests/{requestId}/decline", null);

    public static Task<HttpResponseMessage> CancelFriendRequestAsync(
        this HttpClient client, string requestId) =>
        client.DeleteAsync($"/api/friends/requests/{requestId}");

    public static Task<HttpResponseMessage> UnfriendAsync(this HttpClient client, Guid userId) =>
        client.DeleteAsync($"/api/friends/{userId}");

    public static async Task<FriendsResponseDto> GetFriendsAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/friends");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<FriendsResponseDto>();
    }

    public static async Task<FeedPageDto> GetFeedAsync(this HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/feed{query}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<FeedPageDto>();
    }

    // --- profile and favourites ---------------------------------------------

    public static async Task<ProfileDto> GetMyProfileAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/me/profile");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<ProfileDto>();
    }

    public static async Task<ProfileDto> GetProfileAsync(this HttpClient client, Guid userId)
    {
        var response = await client.GetAsync($"/api/friends/{userId}/profile");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<ProfileDto>();
    }

    public static Task<HttpResponseMessage> SetFavoritesAsync(
        this HttpClient client, params string[] keys) =>
        client.PutAsJsonAsync("/api/me/favorites", new { keys });

    public static async Task<CardDto[]> GetFavoritesAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/me/favorites");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<CardDto[]>();
    }

    /// <summary>Sets the showcase and asserts it took, returning what came back.</summary>
    public static async Task<CardDto[]> SetAndReadFavoritesAsync(
        this HttpClient client, params string[] keys)
    {
        var response = await client.SetFavoritesAsync(keys);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<CardDto[]>();
    }

    /// <summary>
    /// Drives the whole handshake: <paramref name="a"/> asks, <paramref name="b"/>
    /// accepts. Returns nothing — the assertion worth making afterwards is on
    /// <c>GET /api/friends</c>, not on the plumbing.
    /// </summary>
    public static async Task BefriendAsync(
        HttpClient a, Guid aId, HttpClient b, Guid bId)
    {
        var sent = await a.SendFriendRequestAsync(bId);
        Assert.Equal(System.Net.HttpStatusCode.Created, sent.StatusCode);

        var request = await sent.ReadAsAsync<FriendRequestDto>();
        var accepted = await b.AcceptFriendRequestAsync(request.Id);
        Assert.Equal(System.Net.HttpStatusCode.OK, accepted.StatusCode);

        Assert.NotEqual(aId, bId);
    }

    // --- streaming availability (09) ----------------------------------------

    public static Task<HttpResponseMessage> GetAvailabilityAsync(
        this HttpClient client, string key) =>
        client.GetAsync($"/api/titles/{key}/availability");

    public static async Task<AvailabilityDto> ReadAvailabilityAsync(
        this HttpClient client, string key)
    {
        var response = await client.GetAvailabilityAsync(key);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<AvailabilityDto>();
    }

    public static Task<HttpResponseMessage> SetServicesAsync(
        this HttpClient client, string region, params int[] providerIds) =>
        client.PutAsJsonAsync("/api/me/services", new { region, providerIds });

    /// <summary>Sets the viewer's region and services and asserts it took.</summary>
    public static async Task SetAndAssertServicesAsync(
        this HttpClient client, string region, params int[] providerIds)
    {
        var response = await client.SetServicesAsync(region, providerIds);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<WatchProviderDto[]> GetProvidersAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/providers");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<WatchProviderDto[]>();
    }

    public static async Task<MeDto> GetMeAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<MeDto>();
    }

    /// <summary>The provider ids one offer group carries, in the order they arrived.</summary>
    public static int[] Kind(this AvailabilityDto availability, string kind) =>
        availability.Offers
            .FirstOrDefault(o => o.Kind == kind)?
            .Providers.Select(p => p.Id).ToArray() ?? [];

    // --- direct database inspection -----------------------------------------

    /// <summary>
    /// Reads the database behind the API. Several obligations are explicitly about
    /// *stored* state — queue positions, activity rows — which a response body
    /// cannot prove.
    /// </summary>
    public static async Task<T> QueryAsync<T>(
        this WopcornApiFactory factory, Func<WopcornDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<WopcornDbContext>());
    }

    /// <summary>The stored queue as (key, position) pairs, in position order.</summary>
    public static Task<List<(string Key, int? Position)>> QueuePositionsAsync(
        this WopcornApiFactory factory, Guid userId) =>
        factory.QueryAsync(async db =>
            (await db.ListEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.Kind == ListKind.Queue)
                .OrderBy(e => e.Position)
                .Select(e => new { e.TitleKey, e.Position })
                .ToListAsync())
            .Select(e => (e.TitleKey, e.Position))
            .ToList());

    public static Task<List<ActivityEvent>> ActivityAsync(
        this WopcornApiFactory factory, Guid userId) =>
        factory.QueryAsync(db => db.ActivityEvents
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.TitleKey)
            .ToListAsync());

    /// <summary>Every catalog row, for the id-collision assertions.</summary>
    public static Task<List<Title>> TitlesAsync(this WopcornApiFactory factory) =>
        factory.QueryAsync(db => db.Titles.AsNoTracking().OrderBy(t => t.Key).ToListAsync());

    /// <summary>The season rows a series fetch left behind, in season order.</summary>
    public static Task<List<Title>> SeasonsOfAsync(
        this WopcornApiFactory factory, string seriesKey) =>
        factory.QueryAsync(db => db.Titles
            .AsNoTracking()
            .Where(t => t.ParentKey == seriesKey)
            .OrderBy(t => t.SeasonNumber)
            .ToListAsync());
}
