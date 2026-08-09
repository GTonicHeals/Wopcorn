# Wopcorn API Contract

Shared source of truth. Backend plans implement it; frontend plans consume it.
If an implementation needs to deviate, this file is edited **first**.

All routes are under `/api`. All responses are `application/json` unless stated.
All routes except those marked *anonymous* require an authenticated cookie and
return `401` without one (FR-A8).

## Conventions

- The **title key** (string) is the identifier throughout the API — in paths, in
  request bodies, and on every DTO. The client never sees an internal row id.
- `list` path segment is one of `watched` | `watchlist` | `queue`.
- `rating` is an integer `1`–`10` (half-stars). `null` means unrated.
- Dates are ISO-8601 UTC strings.
- Poster/backdrop fields are bare TMDB paths (`/abc123.jpg`). The client builds
  URLs from `GET /api/config` and picks a size per viewport (FR-H6).

### The title key

TMDB's film and TV ids are separate namespaces and they collide — 1396 is
*Mirror* (1975) as a movie and *Breaking Bad* as a series. So the identifier
carries the media type:

```
movie-603          a film
tv-1396            a series
tv-1396-s2         season 2 of that series
```

Grammar: `^(movie|tv)-(\d+)(-s(\d+))?$`. Season numbers are TMDB's own, so `-s0`
is the specials season and is legal. A season is addressed by
`(series id, season number)` because a TMDB season's own `id` is not routable.

One format everywhere: the same string is the primary key, the wire identifier,
and the URL segment. `-` is the separator precisely so the key needs no escaping
in a path. A key that does not parse is `400 validation_failed`, never `404` — a
malformed identifier is a bad request, not a missing title.

### Error shape

Every non-2xx response body:

```jsonc
{ "code": "tmdb_unavailable", "message": "TMDB is not responding. Your lists are unaffected." }
```

`code` is a stable machine string; `message` is user-presentable (NFR-10).
Validation failures use `400` with an additional `errors` map keyed by field.

| code | Status | Meaning |
|---|---|---|
| `validation_failed` | 400 | Body failed validation; see `errors` |
| `unauthenticated` | 401 | No/expired session |
| `forbidden` | 403 | Not friends, or not the owner |
| `not_found` | 404 | Unknown title, user, or entry |
| `display_name_taken` | 409 | FR-A2 |
| `invalid_reset_token` | 400 | Password reset link is wrong, already used, or expired |
| `passkey_failed` | 400 | Passkey attestation/assertion rejected by the server |
| `already_friends` / `request_pending` | 409 | Friend request conflicts |
| `queue_out_of_sync` | 409 | Submitted queue order doesn't match stored membership; response carries the authoritative `keys` |
| `tmdb_unavailable` | 503 | Upstream down and no cached copy (FR-B8) |

## Shared DTOs

```ts
type UserSummary = {
  id: string;            // GUID
  displayName: string;
  avatarUrl: string | null;
};

type ListMembership = {
  watched: boolean;
  watchlist: boolean;
  queue: boolean;
};

// One registered passkey (WebAuthn credential) belonging to the signed-in user.
type PasskeySummary = {
  id: string;            // base64url credential id — also the DELETE path segment
  name: string;
  createdAt: string;     // ISO-8601 UTC
  isBackedUp: boolean;   // synced to a provider, so it survives losing the device
};

type MediaType = "movie" | "series" | "season";

// The unit rendered in every grid, search result, and list row.
type TitleCard = {
  key: string;                      // "movie-603" | "tv-1396" | "tv-1396-s2"
  mediaType: MediaType;
  tmdbId: number;                   // series id for a season
  seasonNumber: number | null;      // season only
  parentKey: string | null;         // season only — its series
  title: string;
  releaseYear: number | null;       // release_date | first_air_date | air_date
  posterPath: string | null;
  tmdbVoteAverage: number | null;   // 0–10, one decimal
  runtimeMinutes: number | null;    // often null for series — see below
  episodeCount: number | null;      // series and season
  seasonCount: number | null;       // series only
  seasonProgress: { watched: number; total: number } | null;   // see below
  genreIds: number[];
  lists: ListMembership;            // for the authenticated user
  myRating: number | null;
};

// One row of a series' Seasons section. Carries the requester's own state so a
// season can be toggled and rated without a second request per season.
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
  cast: { name: string; character: string | null; profilePath: string | null }[]; // max 12
  seasons: SeasonSummary[];         // series only; empty otherwise
  friendsWatched: { user: UserSummary; rating: number | null }[];  // FR-G4
  stale: boolean;   // cached copy served while a refresh failed
};
```

**`seasonProgress` is on the card, not just the detail.** It is non-null only on
a series the requester has watched at least one season of — never `0 / 5`, which
is not progress. It sits on `TitleCard` because a series in a grid has to be able
to say "3 / 5 seasons" without being opened; the server pays one extra grouped
query per page for it, never one per title.

**Runtime is often null, and that is normal.** TMDB's `episode_run_time` is an
array and is frequently empty — Breaking Bad returns `[]`. A film's runtime is
its `runtime`; a series' is `episode_run_time[0] × number_of_episodes` and a
season's is `episode_run_time[0] × episodes.length`, both **null** when the array
is empty. Two consequences the client must honour: the `runtime` sort puts nulls
last in both directions, and a list's runtime total sums only known runtimes, so
the Lists header reads `"92 titles · 214h 51m"` — a count of titles beside the
sum of what is known. It will understate.

**A season's genres are its series' genres.** TMDB season objects carry none.

**Nothing cascades between a series and its seasons.** Marking season 2 watched
does not mark the series watched, and rating the series does not rate its
seasons — they are independent entries, exactly as the three lists are
independent of each other. `seasonProgress` is what the UI renders instead, so
"3 of 5 seasons watched" needs no rule the data cannot support.

## Config (anonymous)

| Verb | Route | Response |
|---|---|---|
| GET | `/api/config` | `{ imageBaseUrl: string, posterSizes: string[], backdropSizes: string[], profileSizes: string[], attribution: { text: string, logoUrl: string } }` |

`attribution` satisfies FR-B9 and must be rendered by the client.

## Auth — `be-01`

| Verb | Route | Body | Response |
|---|---|---|---|
| POST | `/api/auth/register` *(anon)* | `{ email, password, displayName }` | `200 UserSummary` · `400` · `409 display_name_taken` |
| POST | `/api/auth/login` *(anon)* | `{ email, password }` | `200 UserSummary` · `401` |
| POST | `/api/auth/logout` | — | `204` |
| GET | `/api/auth/me` *(anon)* | — | `200 UserSummary` · `401` |
| PUT | `/api/me` | `{ displayName }` | `200 UserSummary` · `409` |
| PUT | `/api/me/avatar` | `multipart/form-data`, field `file` | `200 { avatarUrl }` · `400` |
| DELETE | `/api/me/avatar` | — | `204` |

Login issues a **persistent** cookie (FR-A4). `GET /api/auth/me` is anonymous so
the client can boot without a 401 in the console.

### Password reset

| Verb | Route | Body | Response |
|---|---|---|---|
| POST | `/api/auth/forgot-password` *(anon)* | `{ email }` | `202` |
| POST | `/api/auth/reset-password` *(anon)* | `{ email, token, password }` | `204` · `400 validation_failed` · `400 invalid_reset_token` |

`forgot-password` answers **`202` with an empty body for every input**, including
an unknown or malformed email — the same no-enumeration rule as `login`. Whether
a mail was actually sent is never observable to the caller.

The link is delivered by SMTP when `Smtp:Host` is configured; otherwise the
server logs it at Information. Delivery is a server concern and never changes
this response.

`reset-password` consumes a single-use Identity token. A wrong, expired, or
already-spent token is `400 invalid_reset_token`; a token that is fine but a
password that fails policy is `400 validation_failed` with `errors.password`. A
successful reset does **not** sign the user in — they land back on `/login`.

### Passkeys

Sign-in (anonymous):

| Verb | Route | Body | Response |
|---|---|---|---|
| POST | `/api/auth/passkeys/request-options` *(anon)* | `{ email? }` | `200 { optionsJson }` |
| POST | `/api/auth/passkeys/signin` *(anon)* | `{ credentialJson }` | `200 UserSummary` · `401` |

Management (authenticated):

| Verb | Route | Body | Response |
|---|---|---|---|
| GET | `/api/me/passkeys` | — | `200 PasskeySummary[]` |
| POST | `/api/me/passkeys/creation-options` | — | `200 { optionsJson }` |
| POST | `/api/me/passkeys` | `{ credentialJson, name? }` | `201 PasskeySummary` · `400 passkey_failed` |
| DELETE | `/api/me/passkeys/{id}` | — | `204` · `404` |

`optionsJson` is a **JSON-encoded string**, not an object: it is passed through
verbatim from Identity and handed to `navigator.credentials` after the client
parses it. Encoding it as a string keeps the server from re-serialising a
structure WebAuthn is strict about.

`credentialJson` is likewise the browser credential serialised to a string.

Both options endpoints set a short-lived challenge-state cookie that the matching
`signin`/`POST` call must echo back, so **every passkey request must send
credentials** — the two calls are one exchange.

Omitting `email` from `request-options` asks for a usernameless (discoverable)
credential, which is the normal path; supplying it narrows the allow-list to that
user's passkeys. An unknown email still returns options rather than 404 — no
enumeration here either.

A passkey is an **additional** sign-in method. Every account keeps a password, so
removing the last passkey is always allowed and never locks anyone out.

## Catalog — `be-02`, `08`

| Verb | Route | Query | Response |
|---|---|---|---|
| GET | `/api/titles/search` | `q` (required), `page` (default 1), `type` (repeatable) | `200 { page, totalPages, totalResults, results: TitleCard[] }` |
| GET | `/api/titles/discover/{feed}` | `page`, `type` (repeatable); `feed` ∈ `popular`\|`top-rated`\|`now-playing` | same shape |
| GET | `/api/titles/{key}` | — | `200 TitleDetail` · `400` · `404` · `503` |
| POST | `/api/titles/{key}/refresh` | — | `200 TitleDetail` — forces a re-fetch (FR-B7) |
| GET | `/api/genres` | — | `200 { id, name, mediaTypes: MediaType[] }[]` — the union of the movie and TV genre lists |

Search with a blank `q` returns an empty result set, not a 400.

`type` is repeatable and defaults to all types. On `search` and `discover` only
`movie` and `series` are meaningful; `type=season` is ignored there, since TMDB
has no season search. Search is one upstream request over `/search/multi` with
people discarded, so TMDB's own relevance ordering across the two types is what
the client sees. With both types requested, discover interleaves the two upstream
pages rather than concatenating them, so page 1 is not all films.

Discover maps per type, because TMDB has no single feed spanning both:

| `feed` | movie | series |
|---|---|---|
| `popular` | `/movie/popular` | `/tv/popular` |
| `top-rated` | `/movie/top_rated` | `/tv/top_rated` |
| `now-playing` | `/movie/now_playing` | `/tv/on_the_air` |

`GET /api/genres` returns the union of `/genre/movie/list` and `/genre/tv/list`.
The ids overlap where the names match (Drama 18, Comedy 35) and TV adds its own
(Action & Adventure 10759, Kids 10762, 10763–10768), so the union is
conflict-free; `mediaTypes` says which side each genre came from.

Fetching a **series** detail also populates a summary row for each of its
seasons, from the `seasons[]` array TMDB already returns — so opening a series
costs one upstream request, not one per season. Season *details* are fetched only
when a season is opened.

## Lists — `be-03`

| Verb | Route | Body / Query | Response |
|---|---|---|---|
| GET | `/api/lists/{list}` | `sort`, `dir`, `genre` (repeatable), `decade` (repeatable), `type` (repeatable) | `200 { count, entries: ListEntry[] }` |
| PUT | `/api/lists/{list}/{key}` | `{ alsoRemoveFrom?: ("watchlist"\|"queue")[], watchedOn?: string }` | `200 ListEntry` — idempotent |
| DELETE | `/api/lists/{list}/{key}` | — | `204` — idempotent |

```ts
type ListEntry = {
  title: TitleCard;
  addedAt: string;
  position: number | null;
  watchedOn: string | null;
  rating: number | null;   // the list owner's rating
};
```

`rating` is the rating of the person whose list this is. On your own lists it
repeats `title.myRating`; on `GET /api/friends/{userId}/lists/{list}` it is the
**friend's** rating, while `title.lists` and `title.myRating` still describe the
authenticated user. That split is what lets "my friend gave this 9, and it's
already on my watchlist" render in one pass (be-04 task 3).

`sort` ∈ `added` | `title` | `year` | `runtime` | `score` | `rating`
(`rating` only valid on `watched`). `dir` ∈ `asc` | `desc`, default `desc` for
`added`/`score`/`rating`, `asc` otherwise. `decade` values are the decade start
year (`1990`). `type` ∈ `movie` | `series` | `season`, repeatable, defaulting to
all. Unknown sort/filter values are ignored, not rejected.

`count` is the **unfiltered** total for the list, `type` included — so the header
can say "showing 12 of 84" from the one request.

`alsoRemoveFrom` implements FR-C6 in one round trip.

### Queue ordering

| Verb | Route | Body | Response |
|---|---|---|---|
| PUT | `/api/queue/order` | `{ keys: string[] }` — the complete queue in intended order | `200 { keys: string[] }` |
| POST | `/api/queue/sort` | `{ preset: "added"\|"title"\|"runtime"\|"score", dir?: "asc"\|"desc" }` | `200 { keys: string[] }` |

Both **rewrite stored positions** (FR-D3). The response echoes the authoritative
order so the client can reconcile its optimistic update (FR-D5). `PUT` with a
list that does not exactly match the user's queue membership returns `409
queue_out_of_sync` with the current order in `keys`.

The queue mixes media types in one order — a film, a series and a season sit in
the same list and reorder against each other.

## Ratings — `be-03`

| Verb | Route | Body | Response |
|---|---|---|---|
| PUT | `/api/titles/{key}/rating` | `{ rating: 1..10 }` | `200 ListEntry` — implicitly adds to Watched (FR-E3) |
| DELETE | `/api/titles/{key}/rating` | — | `204` — clears rating, keeps the Watched entry |
| GET | `/api/me/rating-stats` | — | `200 RatingStats` |

Ratings are integers 1–10 for every media type (FR-E2) — a series and a season
are rated exactly as a film is.

```ts
type RatingStats = { count: number; average: number | null; distribution: number[] }; // length 10, index 0 = 1 half-star
```

## Friends — `be-04`

| Verb | Route | Body / Query | Response |
|---|---|---|---|
| GET | `/api/users/search` | `q` | `200 UserSearchResult[]` — display name prefix, excludes self |
| GET | `/api/friends` | — | `200 { friends: Friend[], incoming: FriendRequest[], outgoing: FriendRequest[] }` |
| POST | `/api/friends/requests` | `{ userId }` | `201 FriendRequest` · `409` |
| POST | `/api/friends/requests/{id}/accept` | — | `200 Friend` · `403` |
| POST | `/api/friends/requests/{id}/decline` | — | `204` · `403` |
| DELETE | `/api/friends/requests/{id}` | — | `204` · `403` · `404` — the **sender** withdraws their own pending request |
| DELETE | `/api/friends/{userId}` | — | `204` |
| GET | `/api/friends/{userId}/profile` | — | `200 Profile` · `403` |
| GET | `/api/friends/{userId}/lists/{list}` | same query params as own lists | `200 { count, entries: ListEntry[] }` · `403` |

```ts
type Friend = { user: UserSummary; friendsSince: string; tasteMatch: TasteMatch };
type FriendRequest = { id: string; user: UserSummary; sentAt: string };
type TasteMatch = { score: number | null; sharedCount: number; qualified: boolean };
type UserSearchResult = UserSummary & {
  relationship: "none" | "friends" | "request_sent" | "request_received";
};
```

`qualified` is `false` below the overlap threshold; when `false` the client must
not present `score` as a headline figure (FR-G6). The threshold is **5** shared
rated **titles**, and `score` is `null` only when the pair share none at all.
Overlap is matched on the title key, so a film and a series that happen to share
a TMDB id never collide.

`POST /api/friends/requests` also answers `400 validation_failed` for a request
to yourself and `404 not_found` for an unknown user id.
`POST /api/friends/requests/{id}/accept|decline` answer `404 not_found` when the
request has already been answered or withdrawn.

`accept`/`decline` are the **recipient's** verbs; `DELETE /api/friends/requests/{id}`
is the **sender's** (FR-F1). Each returns `403` to the other party, so the two
sides of a pending request can never act on each other's behalf. Withdrawing
leaves no trace: the pair return to `relationship: "none"` and either may send
again.
`DELETE /api/friends/{userId}` is idempotent — `204` whether or not the
friendship existed.

Every `/api/friends/{userId}/…` read re-verifies the accepted friendship on that
request (NFR-4).

## Profile and favourites

One payload backs the profile screen, whoever is being looked at. Your own
profile and a friend's are the **same page** and therefore the same DTO — the
only differences are `isSelf`, and `tasteMatch`, which is `null` when there is
nobody to compare you against.

| Verb | Route | Body | Response |
|---|---|---|---|
| GET | `/api/me/profile` | — | `200 Profile` — `isSelf: true`, `tasteMatch: null` |
| GET | `/api/me/favorites` | — | `200 TitleCard[]` — the showcase, in its stored order |
| PUT | `/api/me/favorites` | `{ keys: string[] }` | `200 TitleCard[]` · `400 validation_failed` · `404 not_found` |

```ts
type GenreAffinity = { id: number; name: string; count: number };

// Watched-list runtime, split so the client can be honest about it.
type RuntimeOnRecord = {
  minutes: number;         // sum of the runtimes that are KNOWN
  knownTitles: number;     // how many watched titles contributed one
  unknownTitles: number;   // how many had none, and are therefore missing from `minutes`
};

type Profile = {
  user: UserSummary;
  isSelf: boolean;
  memberSince: string;              // ISO-8601 UTC
  stats: RatingStats;
  counts: { watched: number; watchlist: number; queue: number };
  favorites: TitleCard[];           // ordered, at most 6
  topGenres: GenreAffinity[];       // at most 5, most-watched first
  runtime: RuntimeOnRecord;
  friendCount: number;
  recentActivity: ActivityItem[];   // at most 8, newest first — the owner's own
  tasteMatch: TasteMatch | null;    // null on your own profile
};
```

`GET /api/friends/{userId}/profile` returns the same `Profile` with
`isSelf: false` and a real `tasteMatch`, behind the same friendship check as
every other friend-scoped read.

**`recentActivity` is the profile owner's own activity**, which is exactly what
`GET /api/feed` excludes — the feed is other people's news, a profile is this
person's. Both read the same `ActivityEvent` rows, so an undone action vanishes
from both (FR-G7).

**`runtime` never claims more than it knows.** A watched title whose runtime is
null — an ordinary series — contributes nothing to `minutes` and is counted in
`unknownTitles` instead, so the client can render "at least" when that count is
non-zero rather than passing an understatement off as a total.

### The favourites showcase

Up to **6** titles, in an order the owner chooses; the first is the one the
profile takes its backdrop from. `PUT` **replaces the whole showcase** — the body
is the complete intended list, like `PUT /api/queue/order` — so add, remove and
reorder are one route and there is no per-slot state to get out of step. An empty
array clears it.

Any media type may be a favourite: a film, a series, or one season. Every key
must parse (`400 validation_failed`), must not repeat (`400 validation_failed`),
and must already be in the local catalog (`404 not_found`) — favouriting never
reaches TMDB. There is no requirement that a favourite be on any list: removing a
film from Watched does not stop it being a favourite film.

Favourites are visible to friends, on the profile, and nowhere else. Nothing
about a favourite is written to the activity feed.

## Feed — `be-04`

| Verb | Route | Query | Response |
|---|---|---|---|
| GET | `/api/feed` | `cursor` (opaque), `limit` (default 20, max 50) | `200 { items: ActivityItem[], nextCursor: string \| null }` |

```ts
type ActivityItem = {
  id: string;
  user: UserSummary;
  kind: "rated" | "watched" | "added_watchlist" | "added_queue";
  title: TitleCard;
  rating: number | null;   // set when kind === "rated"
  occurredAt: string;
};
```

`kind` is unchanged across media types — the copy that reads "watched" works for
a film, a series, and a season alike.

Keyset pagination — never `OFFSET` over full history (FR-G3). Items vanish when
the underlying action is undone (FR-G7).

`cursor` is opaque: pass back the `nextCursor` you were given, unchanged.
`nextCursor` is `null` exactly when there is nothing older to fetch, which is the
only stop condition an infinite scroll should use. An unparseable cursor is
`400 validation_failed`. `limit` outside `1..50` is clamped, not rejected. The
feed is friends only — your own activity never appears in it.
