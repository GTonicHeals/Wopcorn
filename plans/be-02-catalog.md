# be-02 — TMDB access, local film cache, catalog endpoints

**Executor:** Opus 5 · **Depends on:** be-01 · **Blocks:** be-03, fe-06

Implements FR-B1…FR-B9, NFR-2, NFR-10. Implement exactly the Config and Catalog
sections of [`API-CONTRACT.md`](API-CONTRACT.md).

## Ground rules

- Every TMDB call is server-side. No TMDB key, token, or `api.themoviedb.org`
  URL may appear in anything the client receives (FR-B5).
- Never read TMDB credentials outside `TmdbClient`. Never log them.
- A TMDB failure must never take down an endpoint that could have answered from
  the database (FR-B8).
- No new NuGet packages. `System.Threading.RateLimiting` and
  `Microsoft.Extensions.Caching.Memory` ship with the shared framework.

---

## Task 1 — Options and typed client registration

Create `Wopcorn.Server/Tmdb/TmdbOptions.cs`:

```csharp
public class TmdbOptions
{
    public const string Section = "Tmdb";
    public string? ReadAccessToken { get; set; }   // v4 bearer, api_read scope
    public string? ApiKey { get; set; }            // v3, fallback
    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";
    public string Language { get; set; } = "en-US";
}
```

In `Program.cs`:

```csharp
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<TmdbClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(8);
    if (!string.IsNullOrWhiteSpace(o.ReadAccessToken))
        http.DefaultRequestHeaders.Authorization = new("Bearer", o.ReadAccessToken);
});
```

Credentials are already in user secrets (`dotnet user-secrets list --project
Wopcorn.Server` to confirm). Do not add them to `appsettings.json`. If
`ReadAccessToken` is absent, fall back to appending `api_key={ApiKey}` as a query
parameter; if both are absent, log one warning at startup and let every catalog
call return `503 tmdb_unavailable`.

**Verify:** `dotnet user-secrets list --project Wopcorn.Server` shows both keys; the app starts.

---

## Task 2 — `TmdbClient`

`Wopcorn.Server/Tmdb/TmdbClient.cs`. One method per upstream call, each
returning a strongly-typed record parsed with `System.Text.Json`
(`JsonSerializerOptions { PropertyNamingPolicy = SnakeCaseLower }`).

| Method | Upstream |
|---|---|
| `SearchAsync(string query, int page, CancellationToken)` | `search/movie?query=&page=&include_adult=false` |
| `DiscoverAsync(DiscoverFeed feed, int page, CancellationToken)` | `movie/popular` · `movie/top_rated` · `movie/now_playing` |
| `GetMovieAsync(int id, CancellationToken)` | `movie/{id}?append_to_response=credits` |
| `GetGenresAsync(CancellationToken)` | `genre/movie/list` |

Requirements:

1. **Rate limiting (FR-B8).** A single process-wide
   `TokenBucketRateLimiter` at 50 tokens/second, 50 burst, queue limit 100.
   Acquire a permit before every request.
2. **429 handling.** On `TooManyRequests`, honour `Retry-After` (seconds), wait,
   retry once. On the second failure, throw `TmdbUnavailableException`.
3. **Failure surface.** Any timeout, network error, or 5xx becomes
   `TmdbUnavailableException`. A 404 becomes `null` (not an exception).
4. **No credential leakage.** Log the request path only, never headers or the
   query string when `ApiKey` fallback is in use.

Cache the genre list in `IMemoryCache` for 24 hours and mirror it into the
`Genres` table so filters work with TMDB down.

**Verify:** add a temporary `[AllowAnonymous]` debug endpoint that calls `SearchAsync("dune", 1)`, confirm real results, then **delete it**.

---

## Task 3 — `FilmCacheService` (FR-B6, FR-B7, NFR-2)

`Wopcorn.Server/Films/FilmCacheService.cs`. This is the only type that writes to
the `Films` table. Nothing else calls `TmdbClient` for film data.

Staleness policy — put these in `const`s at the top of the file:

```csharp
static readonly TimeSpan SummaryTtl = TimeSpan.FromDays(14);
static readonly TimeSpan DetailTtl  = TimeSpan.FromDays(7);
```

API:

```csharp
Task<IReadOnlyList<Film>> UpsertSummariesAsync(IEnumerable<TmdbMovieSummary> movies, CancellationToken ct);
Task<IReadOnlyDictionary<int, Film>> GetManyAsync(IEnumerable<int> tmdbIds, CancellationToken ct);
Task<FilmDetailResult> GetDetailAsync(int tmdbId, bool forceRefresh, CancellationToken ct);

// The result must distinguish "TMDB says this film does not exist" from
// "TMDB is unreachable and we have nothing cached" — the controller maps the
// first to 404 and the second to 503, and a nullable tuple cannot carry that.
public record FilmDetailResult(Film? Film, bool Stale, bool NotFound);
// Film != null, Stale = false            → fresh (within TTL, or just fetched)
// Film != null, Stale = true             → upstream failed; cached copy served
// Film == null, NotFound = true          → TMDB returned 404, nothing cached
// Film == null, NotFound = false         → upstream unreachable, nothing cached
```

Behaviour:

- `UpsertSummariesAsync` — called after every search/discover response. Inserts
  unknown films, refreshes the summary columns of known ones, sets
  `SummaryFetchedAt`, and syncs `FilmGenre` rows from `genre_ids`. Use a single
  round trip to load existing rows (`WHERE TmdbId IN (…)`), then one `SaveChanges`.
- `GetManyAsync` — **pure database read, never upstream.** This is what list and
  feed rendering use. A list of 200 films is one query (NFR-2). Include genres
  with `.Include(f => f.Genres)`.
- `GetDetailAsync` — return the cached row when `DetailFetchedAt` is within
  `DetailTtl` and `forceRefresh` is false. Otherwise fetch
  `GetMovieAsync`, fill `Overview`, `RuntimeMinutes`, `BackdropPath`,
  `Director` (crew job `Director`), `CastJson` (first 12 `cast` entries: name,
  character, profile_path), genres, and set `DetailFetchedAt`. **If the upstream
  call throws and a cached row exists, return it with `Stale = true`** — never
  fail a detail view that has data to show (FR-B8, NFR-10). If it throws with no
  cached row, return `FilmDetailResult(null, Stale: true, NotFound: false)` and
  let the controller emit `503`. If `GetMovieAsync` returns `null` (TMDB 404) and
  no cached row exists, return `FilmDetailResult(null, Stale: false, NotFound: true)`
  so the controller emits `404 not_found`.

Films that appear only inside a list or feed (never searched) are still
guaranteed present, because be-03 only ever adds a film to a list via a code path
that has already cached it.

**Verify:** search a title twice; the second call must issue no TMDB request for films already cached (log a counter, or set a breakpoint) while still returning full results.

---

## Task 4 — `FilmMapper` and membership decoration

`Wopcorn.Server/Api/FilmMapper.cs` converts `Film` → `FilmCard` / `FilmDetail`.

The `lists` and `myRating` fields are per-user. Provide:

```csharp
Task<Dictionary<int, (ListMembership Lists, int? Rating)>> LoadUserContextAsync(
    Guid userId, IReadOnlyCollection<int> tmdbIds, CancellationToken ct);
```

implemented as **one** query over `ListEntries` filtered by `UserId` and
`FilmTmdbId IN (…)`, grouped in memory. Every endpoint returning `FilmCard[]`
calls this once for the whole page — never per film (NFR-2, FR-C3).

`releaseYear` is `ReleaseDate?.Year`. `genreIds` comes from the join rows.

**Verify:** a 20-result search issues exactly two database queries beyond the film upsert (context load + genre load), confirmed by enabling `Microsoft.EntityFrameworkCore.Database.Command` logging at `Information` in development.

---

## Task 5 — `FilmsController`

`Wopcorn.Server/Controllers/FilmsController.cs`, route `api/films`, `[Authorize]`.

| Action | Notes |
|---|---|
| `GET search?q=&page=` | Blank/whitespace `q` → `200` with an empty `results` array, no upstream call. Cap `page` at 500 (TMDB's limit). Upsert summaries, then decorate. |
| `GET discover/{feed}?page=` | `feed` must parse to `popular`, `top-rated`, `now-playing`; anything else → `404 not_found`. Cache each feed page in `IMemoryCache` for 30 minutes keyed by `(feed, page)` — this is shared, non-user-specific data, so cache the raw TMDB payload and decorate per user afterwards (FR-B4). |
| `GET {tmdbId}` | `GetDetailAsync(force: false)`. `NotFound` → `404 not_found`; `null` film otherwise → `503 tmdb_unavailable`. Set `stale` from the result. `friendsWatched` is `[]` in this plan — be-04 fills it. |
| `POST {tmdbId}/refresh` | `GetDetailAsync(force: true)` (FR-B7). |

`GET api/genres` lives in the same controller or its own — return the cached
genre list, database-first, so filters survive a TMDB outage.

Wrap `TmdbUnavailableException` centrally: add an exception-handling middleware
or an `IExceptionFilter` that turns it into
`503 { code: "tmdb_unavailable", message: "TMDB is not responding right now. Your lists and ratings are unaffected." }`
(NFR-10). Do not repeat try/catch in every action.

**Verify** with a signed-in cookie jar:

```sh
curl -k -b c.txt 'https://localhost:7173/api/films/search?q=dune'          # 200, results with lists+myRating
curl -k -b c.txt 'https://localhost:7173/api/films/search?q='             # 200, empty results
curl -k -b c.txt  https://localhost:7173/api/films/438631                 # 200 FilmDetail with director+cast
curl -k -b c.txt  https://localhost:7173/api/films/discover/popular       # 200
curl -k -b c.txt  https://localhost:7173/api/films/discover/nonsense      # 404
curl -k           https://localhost:7173/api/films/search?q=dune          # 401
```

Then set `Tmdb:BaseUrl` to an unroutable host and confirm: `search` returns
`503` with the friendly message, and a previously-viewed `GET /api/films/{id}`
still returns `200` with `stale: true`.

---

## Done when

- [ ] No TMDB credential or upstream URL reaches any response body
- [ ] Repeated searches and list renders hit the database, not TMDB (FR-B6)
- [ ] Cached detail is served with `stale: true` during an upstream outage
- [ ] `/api/genres` answers with TMDB unreachable
- [ ] The temporary debug endpoint from task 2 is deleted

## Hand off to be-03 with

The `FilmCacheService` method signatures as built, and the TTL values you settled
on if they differ from this plan.
