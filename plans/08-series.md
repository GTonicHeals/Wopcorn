# 08 — TV series and seasons

**Executor:** Opus 5 · **Depends on:** be-02, be-03, be-04, fe-06, fe-07 ·
**Blocks:** —

Extends the app from films to **titles**: films, TV series, and individual
seasons. Series and seasons are tracked at the same grain as a film — each is a
thing you can put on Watched, Watchlist, or the Queue, and rate.

This is not an additive plan. It re-keys the catalog table and every row that
points at it, so it changes `API-CONTRACT.md`, both test suites, and most of the
client. It is written as one document because the schema decision couples the
two tracks; it can be split at the *Frontend* heading once phase 1 has landed.

## Why the key has to change first

TMDB ids for films and TV are **separate namespaces and they collide**. Checked
against the live API:

| TMDB id | `/movie/{id}` | `/tv/{id}` |
|---|---|---|
| 1396 | *Mirror* (1975) | **Breaking Bad** |
| 1399 | 404 | Game of Thrones |
| 66732 | 404 | Stranger Things |

`Films.TmdbId` is a bare primary key and `ListEntries`, `ActivityEvents`, and
`FilmGenre` all foreign-key to it. Every film-addressed route is a bare int, and
`PUT /api/queue/order` carries `{ tmdbIds: number[] }`. All of it becomes
ambiguous the moment a series can be id 1396 too. Nothing else in this plan can
be built until the key carries the media type.

Seasons make it a three-part key: TMDB addresses a season as
`/tv/{series_id}/season/{season_number}` and a season's own `id` is not
routable, so a season is `(series id, season number)`.

## The title key

One canonical string, used as the primary key, the wire identifier, and the URL
segment:

```
movie-603          a film
tv-1396            a series
tv-1396-s2         season 2 of that series
```

Grammar: `^(movie|tv)-(\d+)(-s(\d+))?$`. Season numbers are TMDB's own, so
`-s0` is the specials season and is legal.

**Why a string PK rather than a composite `(MediaType, TmdbId, SeasonNumber)`:**
three tables foreign-key to the catalog, and a three-column FK triples the
join predicates, the index definitions, and the EF configuration in each one —
for a key that is never queried by its parts alone. A single `TEXT` column keeps
every FK one column wide and every route one segment. The parts stay as their
own columns for filtering and sorting, so nothing is lost but normal form.

Cost, stated plainly: the PK is ~10 bytes instead of 4, which widens four
indexes, and `Key` is derivable from the three columns and so can disagree with
them if written carelessly. Task 1 makes it the only writable identity — the
parts are set from the key, never the reverse.

One format everywhere. Do not invent a second encoding for URLs; `-` is the
separator precisely so the key needs no escaping in a path.

---

## Ground rules

- **`API-CONTRACT.md` is edited first** (task 0), before a line of either track.
  It currently describes what is implemented; the delta below is what it must
  say when this plan is done.
- The acting user is `CurrentUserId`. Unchanged (NFR-3).
- Every write still ensures the title exists in the catalog first, via the
  renamed `TitleCacheService`, and still emits or retracts an `ActivityEvent`.
- **No cascade between a series and its seasons.** Marking season 2 watched does
  not mark the series watched, and rating the series does not rate its seasons.
  They are independent entries, the same way the three lists are independent
  (glossary, §1). What the UI gets instead is `seasonProgress` (task 4), so
  "3 of 5 seasons watched" renders without inventing a rule the data cannot
  support.
- A season's genres are its series' genres. TMDB season objects carry none.
- Ratings stay integers 1–10 for every media type (FR-E2).

---

# Backend

## Task 0 — Rewrite the contract

Apply this delta to `API-CONTRACT.md`. It is the whole of the wire change.

**Conventions section.** Replace "TMDB ids are the film primary key" with: *the
title key is the identifier throughout the API; the client never sees an internal
row id.* Add the grammar above.

**Renamed DTOs** — `FilmCard` → `TitleCard`, `FilmDetail` → `TitleDetail`,
`ActivityItem.film` → `ActivityItem.title`. The rename is mechanical and the
type-checker finds every site on both sides. (Keeping the `Film*` names is a
legitimate cheaper option; it costs a permanent lie in the type names, and every
future reader pays it.)

```ts
type MediaType = "movie" | "series" | "season";

type TitleCard = {
  key: string;                      // "movie-603" | "tv-1396" | "tv-1396-s2"
  mediaType: MediaType;
  tmdbId: number;                   // series id for a season
  seasonNumber: number | null;      // season only
  parentKey: string | null;         // season only — its series
  title: string;
  releaseYear: number | null;       // release_date | first_air_date | air_date
  posterPath: string | null;
  tmdbVoteAverage: number | null;
  runtimeMinutes: number | null;    // see Task 2 — often null for series
  episodeCount: number | null;      // series and season
  seasonCount: number | null;       // series only
  genreIds: number[];
  lists: ListMembership;
  myRating: number | null;
};

type SeasonSummary = {
  key: string;
  seasonNumber: number;
  name: string;
  episodeCount: number | null;
  airDate: string | null;
  posterPath: string | null;
  lists: ListMembership;
  myRating: number | null;
};

type TitleDetail = TitleCard & {
  backdropPath: string | null;
  overview: string | null;
  releaseDate: string | null;
  genres: { id: number; name: string }[];
  director: string | null;          // films; null for series and seasons
  creators: string[];               // series; empty for films
  cast: { name: string; character: string | null; profilePath: string | null }[];
  seasons: SeasonSummary[];         // series only; empty otherwise
  seasonProgress: { watched: number; total: number } | null;   // series only
  friendsWatched: { user: UserSummary; rating: number | null }[];
  stale: boolean;
};
```

**Routes.** Every `{tmdbId}` becomes `{key}`, and the catalog moves from
`/api/films` to `/api/titles`:

| Was | Becomes | Note |
|---|---|---|
| `GET /api/films/search` | `GET /api/titles/search` | adds `type` (repeatable) |
| `GET /api/films/discover/{feed}` | `GET /api/titles/discover/{feed}` | adds `type`; see task 3 |
| `GET /api/films/{tmdbId}` | `GET /api/titles/{key}` | |
| `POST /api/films/{tmdbId}/refresh` | `POST /api/titles/{key}/refresh` | |
| `PUT/DELETE /api/lists/{list}/{tmdbId}` | `PUT/DELETE /api/lists/{list}/{key}` | |
| `PUT/DELETE /api/films/{tmdbId}/rating` | `PUT/DELETE /api/titles/{key}/rating` | |
| `PUT /api/queue/order` `{ tmdbIds: number[] }` | `{ keys: string[] }` | |
| `POST /api/queue/sort` | unchanged verb, response `{ keys }` | |
| `GET /api/lists/{list}` | unchanged | adds `type` (repeatable) |
| `GET /api/genres` | unchanged | returns the union; each genre gains `mediaTypes: MediaType[]` |

`queue_out_of_sync` carries `keys` instead of `tmdbIds`. A key that does not
parse is `400 validation_failed`, never `404` — a malformed identifier is a bad
request, not a missing title.

`type` on `search`, `discover`, and `lists` is repeatable and defaults to all
types. On `search` and `discover` only `movie` and `series` are meaningful;
`type=season` there is ignored, since TMDB has no season search.

**No compatibility shim on the API.** The client ships in the same repo and is
updated in the same change; a parallel `/api/films/*` would be two contracts to
keep honest. The *client router* does keep a redirect (task 10) because
bookmarks are outside the repo.

**Verify:** the contract's route table has no `{tmdbId}` left.

---

## Task 1 — `Title` entity and the migration

`Wopcorn.Server/Data/Entities/Title.cs`, replacing `Film.cs`:

```csharp
public enum MediaType { Movie = 1, Series = 2, Season = 3 }

public class Title
{
    public required string Key { get; set; }         // PK — "tv-1396-s2"
    public MediaType MediaType { get; set; }
    public int TmdbId { get; set; }                  // series id for a season
    public int? SeasonNumber { get; set; }
    public string? ParentKey { get; set; }           // season → its series
    public Title? Parent { get; set; }

    public required string Title_ { get; set; }      // `Name` in EF; see note
    public DateOnly? ReleaseDate { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbVoteAverage { get; set; }
    public int? RuntimeMinutes { get; set; }
    public int? EpisodeCount { get; set; }
    public int? SeasonCount { get; set; }
    public string? Overview { get; set; }
    public string? Director { get; set; }
    public string? CreatorsJson { get; set; }
    public string? CastJson { get; set; }
    public DateTimeOffset SummaryFetchedAt { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }
    public ICollection<TitleGenre> Genres { get; set; } = [];
}
```

Name the display column `Name` on the entity — `Title.Title` inside a class
called `Title` is legal and unreadable. Map it to the existing `Title` column so
the migration does not rewrite the data.

Unique index on `(MediaType, TmdbId, SeasonNumber)`. Index `ParentKey`.
Static `TitleKey.Parse` / `TitleKey.For` are the **only** way a key is produced
or split; nothing else concatenates strings.

**Migration** `SeriesAndSeasons`:

| Table | Change |
|---|---|
| `Films` → `Titles` | add `Key` (PK), `MediaType`, `TmdbId`, `SeasonNumber`, `ParentKey`, `EpisodeCount`, `SeasonCount`, `CreatorsJson`; old `TmdbId` PK becomes the plain `TmdbId` column |
| `ListEntries` | `FilmTmdbId` → `TitleKey` (TEXT, FK → `Titles.Key`) |
| `ActivityEvents` | `FilmTmdbId` → `TitleKey` |
| `FilmGenre` → `TitleGenre` | `FilmTmdbId` → `TitleKey`, PK `(TitleKey, GenreTmdbId)` |

Backfill in the migration: `Key = 'movie-' || TmdbId`, `MediaType = 1`,
`SeasonNumber = NULL` for every existing row, then the same expression for each
child table's `TitleKey`. SQLite rebuilds the table for a PK change, so the
generated migration must be read, not trusted — check the rebuild carries the
data and the FKs across.

**Verify:** against a copy of the dev database, `dotnet ef database update`
preserves every existing row — including the 92 imported watched entries on
`wdiquinzio@gmail.com` — with `Titles.Key` matching `'movie-' || TmdbId`
throughout, and `select count(*) from ListEntries where TitleKey not in (select
Key from Titles)` returning 0.

---

## Task 2 — TMDB client: TV

`ITmdbClient` gains, alongside the existing movie methods:

```csharp
Task<TmdbPage<TmdbMultiSummary>?> SearchMultiAsync(string query, int page, CancellationToken ct);
Task<TmdbSeriesDetail?> GetSeriesAsync(int id, CancellationToken ct);
Task<TmdbSeasonDetail?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken ct);
Task<IReadOnlyList<TmdbGenre>> GetTvGenresAsync(CancellationToken ct);
```

Search uses TMDB's `/search/multi`, **discarding `media_type: "person"`**, rather
than two calls merged by hand: one upstream request per search keeps FR-B6's
"one query, one call" property, and TMDB's own relevance ordering across the two
types beats any merge rule invented here.

Field mapping, because TV does not reuse the movie names:

| Wopcorn | Movie | Series | Season |
|---|---|---|---|
| `Name` | `title` | `name` | `name` (e.g. "Season 2") |
| `ReleaseDate` | `release_date` | `first_air_date` | `air_date` |
| `EpisodeCount` | — | `number_of_episodes` | `episodes.length` |
| `SeasonCount` | — | `number_of_seasons` | — |
| `Director` | crew job `Director` | — | — |
| `CreatorsJson` | — | `created_by[].name` | — |

**Runtime is the trap.** `episode_run_time` is an array and it is frequently
empty — Breaking Bad returns `[]`. The rule:

- Movie: `runtime`.
- Series: `episode_run_time[0] * number_of_episodes`, or **null** when the array
  is empty.
- Season: `episode_run_time[0] * episodes.length`, or null.

Null runtime is therefore normal for series, not exceptional. Two consequences
to implement, not paper over: the `runtime` sort puts nulls last in both
directions, and list runtime totals sum only known runtimes. The Lists header
becomes `"92 titles · 214h 51m"` — count of titles, sum of what is known. It
will understate. That is better than inventing episode lengths, and better than
hiding the total.

**Verify:** `FakeTmdbClient` gains TV counterparts with the same call counters
and `Throw` flag; a series whose `episode_run_time` is `[]` yields a null
runtime and does not throw.

---

## Task 3 — `TitleCacheService`

`FilmCacheService` renamed and widened. Same TTL and staleness contract
(FR-B7, FR-B8, NFR-10); the summary/detail split is unchanged.

- `GetDetailAsync(TitleKey key, bool force, ct)` dispatches on `key.MediaType`
  to `GetMovieAsync`, `GetSeriesAsync`, or `GetSeasonAsync`.
- Fetching a **series detail** also upserts a summary row for each of its
  seasons, from the `seasons[]` array TMDB already returns — so opening a series
  costs one request, not one per season, and the season toggles have rows to
  point at. Season *details* are fetched only when a season screen is opened.
- A season row's `ParentKey` is set on insert. A season may never exist without
  its series row; create the series first in the same transaction.

Discover feeds map per type — TMDB has no single "now playing" across both:

| `feed` | movie | series |
|---|---|---|
| `popular` | `/movie/popular` | `/tv/popular` |
| `top-rated` | `/movie/top_rated` | `/tv/top_rated` |
| `now-playing` | `/movie/now_playing` | `/tv/on_the_air` |

With both types requested, interleave the two pages rather than concatenating,
so page 1 is not all films.

`GenreCatalogService` seeds the **union** of `/genre/movie/list` and
`/genre/tv/list`. The ids overlap where the names match (Drama 18, Comedy 35)
and TV adds its own (Action & Adventure 10759, Kids 10762, and 10763–10768), so
the union is conflict-free — record which media types each genre belongs to for
the `mediaTypes` field.

**Verify:** with a fake returning both, `GET /api/titles/tv-1396` returns
Breaking Bad while `GET /api/titles/movie-1396` returns *Mirror*, and both rows
coexist in `Titles`. Opening the series issues exactly one upstream call and
leaves five season rows behind.

---

## Task 4 — Lists, ratings, queue, feed

Mechanical once the key lands. `ListService`, `RatingStatsService`, the queue
ordering service, and the feed query all swap `int tmdbId` for `TitleKey` and
`Film` for `Title`.

New behaviour, all of it small:

- **Type filter.** `ListQuery` gains `MediaType[] Types`. Applied in the
  database beside the genre and decade filters. `count` stays the **unfiltered**
  total, matching the existing convention that the header can say "showing 12 of
  84" from one request.
- **Queue mixes types** in one order. `PUT /api/queue/order` validates that the
  submitted `keys` exactly match queue membership, as before.
- **`seasonProgress`** on a series' `TitleDetail`: count the requester's watched
  entries whose `ParentKey` is this series, over `SeasonCount`. One grouped
  query, not one per season.
- **Taste match** compares rated entries of any type, matched on `TitleKey`. A
  film and a series never collide because the keys differ. The threshold of 5
  shared rated titles is unchanged (FR-G6).
- **Feed** items carry `title: TitleCard`. `ActivityKind` is unchanged — the
  copy that reads "watched" works for all three types.

**Verify:** a queue holding a film, a series, and a season reorders across all
three; `type=series` on a mixed watchlist filters while `count` still reports the
unfiltered total; a user who rated *Mirror* and another who rated Breaking Bad
share **zero** titles.

---

# Frontend

## Task 5 — Types, api client, key helper

`src/lib/titleKey.ts` — `parse`, `format`, `isSeason`, `parentOf` — mirrors the
server's `TitleKey` and is the only place the format is known on this side.
Unit-test it directly: the grammar is the one thing both tracks must agree on.

`src/api/types.ts` takes the renamed DTOs. Every store and component follows the
type errors.

## Task 6 — `TitleCard`

`FilmCard.vue` → `TitleCard.vue`. The layout does not change; what changes is
what fills it:

- A small type chip on series and season cards. Films get **no** chip — the
  default needs no label, and a chip on every card is noise.
- Series meta line: `2008 · 5 seasons` (not runtime, which is usually null).
  Season: `Season 2 · 13 episodes`.
- Series cards with any watched season show `3 / 5 seasons` in the meta line.
  This is the signed-in user's own state, so it takes the accent — the only
  gold on the card besides their rating.

## Task 7 — Series detail and seasons

`FilmView.vue` → `TitleView.vue`. For a series it grows a **Seasons** section:
one row per season with its own three toggles and star control, driven by the
`seasons[]` array already in the detail response. A season row navigates to that
season's own screen.

Do not cascade in the UI what the server does not cascade in the data: marking
every season watched leaves the series entry alone, and the progress line simply
reads `5 / 5`.

## Task 8 — Type filter, search, palette, table

- Lists filter sheet gains a **Type** group above Genre — checkboxes for Films,
  Series, Seasons, wired to the `type` query param.
- `useFilmSearch` → `useTitleSearch`, unchanged except the endpoint and a
  `type` argument.
- `GlobalSearch.vue` rows gain the same type chip, and route to `/title/{key}`.
- `ListTable.vue` gains a **Type** column on desktop; on mobile the chip in the
  title cell carries it.

## Task 9 — Stores

`films` store → `titles`, keyed by `string` instead of `number`. The shared-map
property that makes a toggle light up everywhere depends on one entry per title,
so the key must be the canonical string — never a parsed tuple, never a number.

`queue` store's optimistic reconciliation moves from `number[]` to `string[]`;
`moveWithin` is generic already.

## Task 10 — Router

`/film/:tmdbId` → `/title/:key`, with `:key` constrained to the grammar so a
malformed key renders `NotFoundView` rather than firing a request.

Keep a **redirect** from `/film/:tmdbId` to `/title/movie-:tmdbId`. The API gets
no shim; the router does, because bookmarks and the links in already-sent
password-reset-era mail are outside this repo's control. One line, permanent.

---

## Test obligations

Extending `00-testing.md`:

**Server** — the id-collision case is the headline test: movie 1396 and tv 1396
coexist as separate rows, separate list entries, separate ratings. Then: key
parsing rejects `tv-abc`, `movie-1-s2`, and the empty string with `400`; a series
fetch leaves season rows; null-runtime series sort last; the type filter leaves
`count` unfiltered; a mixed queue reorders; season and series entries are
independent in both directions.

**Client** — `titleKey` round-trips every form including `-s0`; `TitleCard`
renders the right meta line per media type; season progress shows only on series
with at least one watched season; the store keys by string and a toggle on a
season does not mark its series.

The existing 196 server and 138 client tests mostly compile-break on the rename
rather than fail on behaviour. Fix them by following the type errors; a test that
needs *thought* to update is a test that found a real behaviour change, and that
is the signal to slow down.

---

## Sequencing

```
0 contract ── 1 schema ── 2 tmdb ── 3 cache ── 4 lists/ratings/queue/feed
                                       │
                                       └── 5 types ── 6 card ── 7 detail ── 8 filters ── 9 stores ── 10 router
```

Phases 0–1 are the risky part and land on their own: after task 1 the app still
works, still films-only, with every row re-keyed. That is the checkpoint worth
verifying against a database copy before anything else is built on it.

Tasks 5–10 cannot start before task 0 fixes the contract, but need only tasks
0–3 of the backend to run against.

## Decisions taken by default

Override any of these before task 0; each is cheap now and expensive later.

| # | Decision | Alternative |
|---|---|---|
| D-1 | String title key as PK | Composite `(MediaType, TmdbId, SeasonNumber)` — normal form, three-column FKs in three tables |
| D-2 | No cascade between series and seasons | Watching every season marks the series watched — needs a rule for what happens when TMDB adds a season |
| D-3 | Rename `Film*` DTOs to `Title*` | Keep the old names, accept that they lie |
| D-4 | Series runtime = episode length × episode count, null when unknown | Omit runtime for series entirely; drops the `runtime` sort for them |
| D-5 | `/search/multi` with people discarded | Two calls merged locally — more control over the mix, two upstream requests per keystroke |
| D-6 | Episodes are **not** tracked | Episode-level progress is a different product; it would need its own entity, its own screens, and a sync rule for "next up" |
