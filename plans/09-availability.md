# 09 — Where to watch

**Executor:** Opus 5 · **Depends on:** 08 (built) · **Blocks:** —

Answers the question the Queue currently cannot: **of the titles I have already
put in order, which ones can I actually watch tonight?**

Adds streaming availability from TMDB's `/watch/providers` (JustWatch data) —
which services carry a title, in the viewer's region — plus the two pieces of
user state that make it actionable: the region they are in and the services they
pay for. The payoff is a filter on the Queue, not a logo strip on the detail
page.

This is an additive plan. It changes no existing route, no existing column, and
no existing DTO field. It adds three tables, one nullable-defaulted column on
`AspNetUsers`, four fields across two DTOs, three routes, and the app's first
background service.

## Why this, and why now

`REQUIREMENTS.md` §5 excludes "streaming-provider availability, price tracking,
or purchase links". This plan re-opens exactly the first of those three and
leaves the other two closed — TMDB's providers payload carries no prices, and
the only link it gives is JustWatch's own page, which is where the "purchase
link" exclusion is honoured rather than violated.

The reason to re-open it: the Queue is the most heavily engineered surface in the
app — explicit positions, drag reorder, sort presets that rewrite storage,
optimistic reconciliation, position numerals reserved as a visual language. All
of that answers *what is next*. None of it answers *what is watchable*, so the
user leaves the app at the exact moment the app was built to serve, and a queue
that cannot be acted on decays into a second watchlist.

## What is being asked of TMDB

| Endpoint | Gives |
|---|---|
| `/movie/{id}/watch/providers` | `results` keyed by ISO-3166-1 region |
| `/tv/{id}/watch/providers` | same shape |
| `/watch/providers/movie?watch_region=XX` | the provider directory |
| `/watch/providers/tv?watch_region=XX` | same, TV side |

Each region entry is `{ link, flatrate[], rent[], buy[], free[], ads[] }`, and
each offer is `{ provider_id, provider_name, logo_path, display_priority }`.

Three properties of that payload shape this plan:

- **There is no season endpoint.** TMDB exposes providers for `movie` and `tv`
  only, and this app's grain goes one level deeper. A season resolves through
  `ParentKey`, the same way its genres already do.
- **It is region-scoped.** Availability with no region is not approximate, it is
  wrong. Region is therefore required state, not a preference.
- **It churns.** Catalogues change weekly. A 24-hour TTL, not the 7 days
  `DetailTtl` gives a detail row.

---

## Ground rules

- **`API-CONTRACT.md` is edited first** (task 0).
- The acting user is `CurrentUserId`, always (NFR-3). Region and services are
  read from the authenticated user, never from a query parameter — an
  unauthenticated caller has no region and gets no availability.
- **Availability never fails a page.** A title detail must render with the
  availability block absent. `TmdbUnavailableException` from a providers fetch is
  caught and swallowed to "unknown", never surfaced as `503` — unlike a detail
  fetch, which legitimately 503s (FR-B8, NFR-10).
- **"Not fetched" and "fetched, nothing available" are different answers.**
  `TitleCacheService` already draws this distinction for genres (`NoGenres` — "this
  payload said nothing about genres", never "it has none"). Availability needs it
  more sharply: a title genuinely on no service in Belgium must not be re-fetched
  every request forever.
- **No JSON blobs.** Offers are queried, not just displayed — the Queue filter is
  a join. Normalised tables, no `json_extract`, no raw SQL (NFR-7).
- Price tracking and per-service deep links stay out of scope. The only outbound
  link is JustWatch's `link`, verbatim.

---

# Backend

## Task 0 — Extend the contract

Apply this delta to `API-CONTRACT.md`.

**`TitleCard` gains one field:**

```ts
type TitleCard = {
  // ...unchanged...
  availableOn: number[];   // TMDB provider ids, subscription only, viewer's region
};
```

`availableOn` lists **only the providers the viewer themself has configured**
that carry this title on subscription in their region. It is `[]` when the viewer
has no services set, when availability has not been fetched, and when the title
is on none of them — three states the card does not distinguish, because the card
renders the same in all three.

It sits on `TitleCard` for the same reason `seasonProgress` does, and is paid for
the same way: **one extra grouped query per page, never one per title** (NFR-2).

**`Profile` and the friend-scoped list reads are unchanged.** `availableOn` is
computed for the *authenticated viewer* everywhere it appears, exactly as
`title.lists` and `title.myRating` already are on a friend's list — a friend's
subscriptions are their business, and a badge saying "you can watch this" must
mean *you*.

**New DTOs:**

```ts
type WatchProvider = {
  id: number;              // TMDB provider_id
  name: string;
  logoPath: string | null; // bare TMDB path, like posters
};

type OfferKind = "flatrate" | "free" | "ads" | "rent" | "buy";

type TitleAvailability = {
  region: string;                  // ISO-3166-1 alpha-2, the viewer's
  fetchedAt: string | null;        // null = never fetched; the block renders as unknown
  link: string | null;             // JustWatch page for this title in this region
  offers: { kind: OfferKind; providers: WatchProvider[] }[];  // ordered flatrate, free, ads, rent, buy
};
```

**New routes:**

| Verb | Route | Query / Body | Response |
|---|---|---|---|
| GET | `/api/titles/{key}/availability` | — | `200 TitleAvailability` · `400` · `404` |
| GET | `/api/providers` | — | `200 WatchProvider[]` — the directory for the viewer's region |
| PUT | `/api/me/services` | `{ region: string, providerIds: number[] }` | `200 { region, providerIds }` · `400 validation_failed` |

`GET /api/titles/{key}/availability` on a season returns its **series'**
availability, with the season's own key echoed nowhere — the response describes
where the show can be watched.

`PUT /api/me/services` **replaces the whole set**, like `PUT /api/queue/order` and
`PUT /api/me/favorites`. `region` must be two uppercase letters and must appear in
the directory TMDB publishes; unknown provider ids are `400 validation_failed`,
not silently dropped, because a silently dropped service is a filter that lies.

`GET /api/lists/{list}` gains **`service`** (repeatable, provider id) alongside
`genre`, `decade` and `type`. `service` with no ids is ignored. `count` stays the
**unfiltered** total, as with every other filter.

`GET /api/me` gains `region: string | null` and `providerIds: number[]`.

**`/api/config`** gains `logoSizes: string[]` (`["w45","w92","w154","w185","original"]`)
and a second attribution string:

```ts
attribution: {
  text: string;
  logoUrl: string;
  availabilityText: string;   // "Streaming availability data provided by JustWatch."
}
```

Text only. No second logo file — `wwwroot/tmdb-logo.svg` has been outstanding
since FR-B9 and one unshipped trademarked asset is enough.

**Error codes:** none added. An unknown provider id or a malformed region is
`validation_failed`.

**Verify:** the contract's route table lists three new routes and no existing row
has changed.

---

## Task 1 — Schema and migration

Three entities in `Wopcorn.Server/Data/Entities/`:

```csharp
public enum OfferKind { Flatrate = 1, Free = 2, Ads = 3, Rent = 4, Buy = 5 }

// Mirrors TMDB's provider directory, the way Genres mirrors the genre lists.
public class WatchProvider
{
    public int TmdbProviderId { get; set; }      // PK
    public required string Name { get; set; }
    public string? LogoPath { get; set; }
    public int DisplayPriority { get; set; }
}

// One row per (title, region) we have ASKED about. Its existence is the answer
// to "have we looked?"; zero TitleOffer rows beside it means "looked, nothing".
public class TitleAvailability
{
    public required string TitleKey { get; set; } // PK part, FK -> Titles.Key
    public required string Region { get; set; }   // PK part
    public string? JustWatchLink { get; set; }
    public DateTimeOffset FetchedAt { get; set; } // UtcInstantConverter — see below
}

public class TitleOffer
{
    public required string TitleKey { get; set; } // PK part
    public required string Region { get; set; }   // PK part
    public int ProviderId { get; set; }           // PK part, FK -> WatchProvider
    public OfferKind Kind { get; set; }           // PK part
}
```

And on `AppUser`:

```csharp
public string? Region { get; set; }               // ISO-3166-1 alpha-2, null until set
```

plus a `UserWatchProvider` join `(UserId, ProviderId)`.

`TitleAvailability.FetchedAt` **must** carry `HasConversion<UtcInstantConverter>()`.
The warmer in task 4 orders by it, and SQLite's provider throws
`NotSupportedException` on `ORDER BY` over a `DateTimeOffset`. This is the exact
trap the converter exists for, and it fails at query time, not build time.

Index `TitleOffer` on `(Region, ProviderId)` — that is the direction the Queue
filter reads it, not by title.

**Migration** `Availability`. Purely additive: four `CreateTable`s and one
`AddColumn`. No backfill, no table rebuild, nothing to read carefully — the
opposite of `SeriesAndSeasons`.

**Verify:** `dotnet ef database update` against a copy of the dev database adds
the tables and leaves every existing row untouched; `AspNetUsers.Region` is null
for every existing user and nothing reads it as anything but "not set yet".

---

## Task 2 — TMDB client

`ITmdbClient` gains two methods:

```csharp
Task<TmdbWatchProviders?> GetWatchProvidersAsync(
    MediaType mediaType, int tmdbId, CancellationToken ct);

Task<IReadOnlyList<TmdbProviderDirectoryEntry>> GetProviderDirectoryAsync(
    MediaType mediaType, string region, CancellationToken ct);
```

`mediaType` is `Movie` or `Series` only. Passing `Season` is a programming error
— throw `ArgumentOutOfRangeException`; the caller's job is to resolve the parent
before it gets here, and a silent fallback inside the client would hide the one
mapping worth being explicit about.

`TmdbWatchProviders` deserialises `results` as
`Dictionary<string, TmdbRegionOffers>`. Deserialise **all** regions and store only
the ones asked for — the payload arrives whole regardless, and a second user in a
second region would otherwise re-fetch a response already in hand. Storing every
region TMDB returns is roughly 60 rows per title; storing one is a guaranteed
second request the moment anyone sets a different region.

A 404 from providers means "no data for this title", not "no such title" — return
null and let the caller record an empty fetch. It must never be conflated with the
detail 404 that produces `not_found`.

**Verify:** `FakeTmdbClient` gains both methods with the same per-method call
counters and honours `Throw`; asking for a season's providers throws rather than
silently returning the series'.

---

## Task 3 — `AvailabilityService`

New service in `Wopcorn.Server/Catalog/`, sitting beside `TitleCacheService` and
following its shape: one type owns the `TitleAvailability` / `TitleOffer` /
`WatchProvider` tables, and nothing else writes them.

```csharp
public static readonly TimeSpan AvailabilityTtl = TimeSpan.FromHours(24);
public static bool IsFresh(DateTimeOffset fetchedAt, DateTimeOffset now) =>
    now - fetchedAt < AvailabilityTtl;
```

`GetAsync(TitleKey key, string region, ct)`:

1. Resolve seasons to `ParentKey`. A season with no parent row is impossible by
   construction (08, task 3) — assert it rather than handling it.
2. Fresh row within TTL → return what is stored, no upstream call.
3. Otherwise fetch, replace that `(title, region)`'s offers in one transaction,
   stamp `FetchedAt`.
4. Upstream failure → return the **stale** stored rows if there are any, or an
   `TitleAvailability` with `fetchedAt: null` if there are not. Never throws.

`EnsureDirectoryAsync(region, ct)` mirrors the union of the movie and TV provider
directories into `WatchProvider`, exactly as `GenreCatalogService.EnsureAsync`
does for genres — **including merging against `db.WatchProviders.Local`**, because
the same provider id appears in both the movie and TV lists and EF refuses a
second entity with a key it is already tracking. This is the identical bug
`GenreCatalogService` documents; it will recur here verbatim if the merge is
skipped.

`AvailableOnAsync(Guid userId, IReadOnlyCollection<string> titleKeys, ct)` is the
card path: one grouped query joining `TitleOffer` to the user's
`UserWatchProvider` set, filtered to `Kind == Flatrate` and the user's region,
returning `Dictionary<string, int[]>`. **Short-circuit to empty when the user has
no services configured** — the common case before setup, and it must cost zero
queries. Seasons map to their parent's key on the way in and back to their own on
the way out.

**Verify:** a title fetched twice inside 24h issues one upstream call; a title
with no offers in the region records a fetch and does not re-fetch; a TMDB outage
returns stale rows rather than throwing; a user with no services triggers no query.

---

## Task 4 — The warmer

The problem this task exists for, stated plainly: **availability is fetched when
a title is opened, and nobody opens the titles already in their queue.** Without
warming, `availableOn` is empty for most rows and the filter in task 7 is a
feature that shows nothing.

`AvailabilityWarmer : BackgroundService` — the first background service in this
app, which is the real architectural cost of this plan and the reason it gets a
task rather than a paragraph.

- Working set: titles on **any user's Queue or Watchlist**, in the regions those
  users have set. Watched is excluded — it is the largest list and the one nobody
  needs availability for.
- Order by `FetchedAt` ascending, nulls first. Take a bounded batch per pass.
- **One request per second, hard.** FR-B8 assumes ~50/s; this uses 2% of it and
  will never be the reason a user's search is throttled.
- One pass every 15 minutes, plus one on startup after a short delay.
- Wrap every pass in try/catch. A warmer that crashes takes the host down with it,
  and the app is fully usable with no availability data at all.

**`BackgroundService` is a singleton and `WopcornDbContext` is scoped.** Inject
`IServiceScopeFactory` and open a scope per pass. Injecting the context directly
compiles and then throws at resolution.

Scale check: tens of users with queues of tens of titles is a working set in the
low hundreds. A full pass is minutes at 1 req/s, and the 24-hour TTL means steady
state is a few hundred requests a day.

**Disable it in tests.** `WopcornApiFactory` must remove the hosted service, for
the same reason it blanks `Smtp:Host` — a test run must not have a background
thread making its own calls through the fake client and racing the assertions.
Add the removal beside the SMTP override, with a comment saying why.

**Verify:** a factory with the warmer enabled and a fake client warms exactly the
queued titles and never a watched-only one; the fake's call counter shows the rate
cap held; `Throw` on the fake does not bring down the host.

---

## Task 5 — Endpoints and wiring

- `AvailabilityController` — `GET /api/titles/{key}/availability`, `GET /api/providers`.
  Both authenticated; both read region from the user, `400 validation_failed` if
  the user has not set one yet, with `errors.region` so the client can route to
  settings rather than showing a dead block.
- `MeController` — `PUT /api/me/services`, and `region` / `providerIds` on the
  existing `GET`.
- `TitleMapper` — populate `availableOn` from a `Dictionary<string, int[]>` passed
  in by the caller, defaulting to `[]`. Mapper stays synchronous and does no I/O;
  every list/search/discover/feed/profile action calls `AvailableOnAsync` once for
  its page and hands the result in. **Audit every call site** — a missed one
  silently renders empty badges rather than failing.
- `ListService` — `ListQuery` gains `int[] ServiceIds`, applied beside the
  existing genre/decade/type filters via a join on `TitleOffer`, restricted to
  `Flatrate`. `count` stays unfiltered.
- `ConfigController` — the two new fields. Hardcoded, like the rest of it.

**Verify:** `service=8` on a queue returns only titles with a Netflix flatrate
offer in the user's region while `count` still reports the whole queue; a user
with no region gets `400` with `errors.region` from both availability routes and
`200` from everything else.

---

# Frontend

## Task 6 — Settings: region and services

`MeView.vue` gains a **Where you watch** section: a region select, and below it
the provider directory for that region as a multi-select grid of logos.

This is setup, and setup nobody completes is a feature nobody has. Two things
make it likelier: default the region from `navigator.language`'s region subtag as
a *pre-selection the user confirms* (never silently), and sort the directory by
TMDB's `display_priority`, so the eight services someone might actually have are
above the fold and the long tail is behind a "show all".

The services store is small enough to live on the existing `auth` store beside
the user — it is user identity, not a fourth cache.

## Task 7 — The Queue filter

The payoff. On `ListsView.vue`, when the viewer has at least one service
configured, the filter sheet gains a **Streaming** group above Type, and the
Queue and Watchlist headers gain a one-tap **On my services** chip that applies
`service=` for every configured id at once.

**When no services are configured, none of this renders.** No empty group, no
disabled chip, no nag — the filter appears when it can do something, and until
then the Queue looks exactly as it does today.

The chip is the signed-in user's own state and therefore takes the **accent**,
consistent with every other piece of gold in this app.

## Task 8 — Provider badges on the card

`TitleCard.vue` renders `availableOn` as up to three provider logos at `w45` in
the meta row, with a `+N` when there are more.

**These logos are the only thing in the interface not drawn from `tokens.css`.**
They are third-party brand marks in their own colours, and that is exactly why
they must be small, confined to one row, and never adjacent to the accent — a
Netflix red beside the gold is two competing signals in a design that has spent
considerable effort ensuring gold means one thing. Render them at 20 px, in a
neutral-bordered row, after the meta line.

Empty `availableOn` renders **nothing at all**. No skeleton, no "not available" —
the array cannot distinguish "unknown" from "on none of your services", so it
must not claim either.

## Task 9 — `WhereToWatch.vue` on the detail page

Below the hero, above the cast. Flatrate providers first at full size, then
rent/buy collapsed behind a disclosure — the common case is "is it included",
not "what does it cost".

- Region label on the block, so a wrong answer is legible as a wrong region
  rather than a broken app.
- "Powered by JustWatch", linking to the `link` from the payload, satisfying the
  attribution the availability data comes with.
- `fetchedAt: null` renders "Availability unknown" with a retry, never an empty
  section (NFR-10).
- Fetched **after** the detail render, not with it. The page must not wait on it.

## Task 10 — Types, client, store

`src/api/types.ts` takes the new DTOs and the `availableOn` field.
`src/api/client.ts` takes the three routes.

`src/stores/config.ts` caches the provider directory — it is per-region reference
data that changes monthly and is read by three screens; refetching it per screen
is the kind of thing NFR-2 exists to prevent.

Availability responses are cached per title key in the `titles` store beside the
detail, keyed the same way, so navigating back to a title does not refetch.

---

## Test obligations

Extending `00-testing.md`.

**Server** — the headline test is the season fallback: `GET
/api/titles/tv-1396-s2/availability` returns the series' providers and issues the
series' upstream call. Then: a second request inside 24h issues none; a title with
zero offers records a fetch and is not re-fetched; a TMDB outage returns stale
rows and never a 503; a user with no region gets `400 errors.region`; a user with
no services gets `availableOn: []` everywhere with no extra query; `service=`
filters the queue while `count` stays unfiltered; the provider directory merges
the movie and TV lists without the `Local`-tracking failure; `PUT /api/me/services`
rejects an unknown provider id; a friend's list carries the **viewer's**
`availableOn`, not the friend's.

**Client** — the badge renders nothing for an empty array; the filter chip is
absent with no services configured and accented with some; the provider grid sorts
by display priority; the detail block renders "unknown" rather than empty for a
null `fetchedAt`.

The existing 246 server and 224 client tests should be untouched by this plan. Any
that break did so because something additive turned out not to be — stop and find
out which, rather than updating the test.

---

## Sequencing

```
0 contract ── 1 schema ── 2 tmdb ── 3 service ──┬── 4 warmer
                                                └── 5 endpoints ──┬── 6 settings ── 7 filter
                                                                  └── 8 badges ── 9 detail ── 10 types
```

Tasks 0–3 and 5 are a complete, shippable feature on their own: availability on
the detail page, fetched on open. **Ship that first and use it.** Task 4 is the
only part that introduces a background thread, and it is worth having the data
model in production for a few days before adding something that writes to it
unattended.

Tasks 6–10 need only 0–3 and 5 to run against.

## Decisions taken by default

Override any of these before task 0; each is cheap now and expensive later.

| # | Decision | Alternative |
|---|---|---|
| D-1 | Normalised `TitleOffer` rows | A JSON blob per (title, region) — simpler to write, but the Queue filter is a join and would need raw SQL, against the standing rule |
| D-2 | Store **every** region TMDB returns, serve one | Store only the requested region — a third of the storage, a guaranteed refetch per new region |
| D-3 | Availability is a **separate** route, not a `TitleDetail` field | Fold it in — one request instead of two, at the cost of coupling it to `DetailTtl` and letting a providers failure delay the page |
| D-4 | `availableOn` on `TitleCard` is **flatrate only** | Include rent/buy — but "I can watch this now" and "I can pay £3.49 to watch this now" are different claims and one badge cannot make both |
| D-5 | Seasons resolve to their series | Per-season availability — TMDB does not expose it at all |
| D-6 | A background warmer | On-demand only: correct, cheap, and leaves the Queue filter matching almost nothing, which is the feature not working |
| D-7 | Region is per-user | Per-deployment — simpler, wrong the first time someone travels or the group spans a border |
| D-8 | Availability, not prices or per-service deep links | Both remain excluded by §5; TMDB carries neither |
