# be-03 — Lists, queue ordering, ratings

**Executor:** Opus 5 · **Depends on:** be-01, be-02 · **Blocks:** be-04, fe-06

Implements FR-C1…FR-C7, FR-D1…FR-D5, FR-E1…FR-E6, NFR-3. Implement exactly the
Lists, Queue, and Ratings sections of [`API-CONTRACT.md`](API-CONTRACT.md).

## Ground rules

- The acting user is `CurrentUserId` from `ApiControllerBase`. **Never** accept a
  user id from a route, query, or body (NFR-3).
- Every write that touches a film must ensure the film exists in the `Films`
  table first — call `FilmCacheService.GetDetailAsync(tmdbId, force: false)` and
  return `404 not_found` when the result has `NotFound = true`. (A `null` film
  with `NotFound = false` means TMDB is unreachable with nothing cached — let the
  `TmdbUnavailableException` path surface `503` as usual.) This is what
  guarantees be-02's "list rendering never goes upstream" property.
- Every write also emits or retracts an `ActivityEvent` (task 6). Do not skip
  this because be-04 is not written yet — FR-G7 depends on it being correct at
  write time.
- Mutating endpoints return the resulting `ListEntry` so the client can reconcile
  without a refetch.

---

## Task 1 — `ListService`

`Wopcorn.Server/Lists/ListService.cs`. All list mutation goes through it; the
controller only validates and maps.

```csharp
Task<ListEntry> AddAsync(Guid userId, int tmdbId, ListKind kind,
                         IReadOnlyCollection<ListKind> alsoRemoveFrom, DateOnly? watchedOn, CancellationToken ct);
Task RemoveAsync(Guid userId, int tmdbId, ListKind kind, CancellationToken ct);
Task<IReadOnlyList<ListEntry>> GetAsync(Guid userId, ListKind kind, ListQuery query, CancellationToken ct);
```

`AddAsync` rules:

- **Idempotent.** An existing entry is returned unchanged except that
  `watchedOn` is updated when supplied. Do not bump `AddedAt` on a repeat add.
- Adding to `Queue` assigns `Position = (max existing position) + 1`, appending
  to the end (FR-D1).
- `alsoRemoveFrom` removes the named entries in the same transaction (FR-C6) and
  retracts their activity events.
- The three lists are independent: adding to Watched never implicitly removes
  from Watchlist or Queue unless `alsoRemoveFrom` says so (glossary, §1).

`RemoveAsync` rules:

- Idempotent — removing an absent entry is `204`, not `404`.
- Removing a Queue entry **compacts positions** of the remaining entries so they
  stay contiguous from 0.
- Removing a Watched entry discards its rating.

Wrap multi-step mutations in a transaction
(`await using var tx = await db.Database.BeginTransactionAsync(ct)`).

**Verify:** double-add returns the same `addedAt`; removing the middle of a 3-item queue leaves positions `0,1`.

---

## Task 2 — Sorting and filtering (FR-C4, FR-C5)

`ListQuery` record: `{ string? Sort, string? Dir, int[] GenreIds, int[] Decades }`.

Apply in the database, not in memory:

| `sort` | Order by |
|---|---|
| `added` (default) | `AddedAt` |
| `title` | `Film.Title` |
| `year` | `Film.ReleaseDate` |
| `runtime` | `Film.RuntimeMinutes` |
| `score` | `Film.TmdbVoteAverage` |
| `rating` | `Rating` — **only valid when `kind == Watched`**; on other lists fall back to `added` |

Default direction: `desc` for `added`, `score`, `rating`; `asc` for the rest.
Nulls sort last in both directions — order by `x == null` first, then `x`.

For the `Queue` list, ignore `sort` entirely and always order by `Position`
(FR-D1). The queue's sort presets are a **write** operation (task 4), not a view.

Filters:

- `genre` — repeatable; entries matching **any** given genre
  (`e.Film.Genres.Any(g => genreIds.Contains(g.GenreTmdbId))`).
- `decade` — repeatable; decade start year. Translate to
  `ReleaseDate.Value.Year / 10 * 10` comparisons that EF can translate; if the
  provider cannot translate it, compare against explicit year ranges built in C#.

Unknown values are ignored, never rejected.

**Verify:** `GET /api/lists/watched?sort=rating&dir=asc&genre=878&decade=1980&decade=1990` returns only 80s/90s sci-fi ordered by ascending rating, and the SQL contains the `ORDER BY` (check the command log).

---

## Task 3 — `ListsController`

Route `api/lists`. Bind `{list}` through a helper that maps
`watched|watchlist|queue` to `ListKind` and returns `404 not_found` otherwise.

| Action | Behaviour |
|---|---|
| `GET {list}` | `{ count, entries }`. `count` is the unfiltered total for the list, `entries` is the filtered page — the list view shows both "42 films" and the filtered result (FR-C4). |
| `PUT {list}/{tmdbId}` | Body optional. `alsoRemoveFrom` strings map to `ListKind`; ignore unknown values. `watchedOn` only meaningful for `watched`. Returns `200 ListEntry`. |
| `DELETE {list}/{tmdbId}` | `204`. |

**Verify:**

```sh
curl -k -b c.txt -X PUT https://localhost:7173/api/lists/watchlist/438631      # 200
curl -k -b c.txt -X PUT https://localhost:7173/api/lists/watched/438631 \
     -H 'Content-Type: application/json' -d '{"alsoRemoveFrom":["watchlist"]}' # 200, watchlist gone
curl -k -b c.txt https://localhost:7173/api/lists/watchlist                    # count 0
curl -k -b c.txt -X DELETE https://localhost:7173/api/lists/queue/438631       # 204 (absent entry)
```

---

## Task 4 — `QueueController` (FR-D2…FR-D5)

Route `api/queue`.

**`PUT order`** — body `{ tmdbIds: number[] }`, the complete queue in intended
order.

1. Load the user's queue entries.
2. If the submitted set is not exactly equal to the stored set (same members, no
   duplicates), return `409 queue_out_of_sync` with the current authoritative
   order in `tmdbIds`. This is what lets the client recover from a stale
   optimistic update instead of corrupting the order (FR-D5).
3. Otherwise assign `Position = index` for each id, save in one transaction, and
   echo the order back.

**`POST sort`** — body `{ preset, dir? }` where `preset` ∈ `added` | `title` |
`runtime` | `score`. Compute the new order server-side using the same
null-handling as task 2, **write the positions**, and return the resulting
order. This rewrites stored state; it is not a view (FR-D3). Entries remain
hand-draggable afterwards because positions are just integers (FR-D4).

Reordering emits no activity events — position is private, not feed-worthy.

**Verify:** apply `POST /api/queue/sort {"preset":"title"}`, then `GET /api/lists/queue` returns title order; then `PUT /api/queue/order` with a hand-shuffled array and confirm it sticks; then `PUT` with one id missing and confirm `409` carrying the real order.

---

## Task 5 — `RatingsController` (FR-E1…FR-E6)

Route `api/films/{tmdbId}/rating` — put it in its own controller to keep
`FilmsController` read-only.

- `PUT` — body `{ rating }`, validated as an integer in `[1,10]`. Anything else
  is `400 validation_failed` with the message "Rating must be between 1 and 10
  half-stars." **If no Watched entry exists, create one** (FR-E3) with
  `AddedAt = now`; then set the rating. Return the resulting `ListEntry`.
- `DELETE` — clear `Rating` to `null`, keep the Watched entry (FR-E4), retract
  the `Rated` activity event, `204`. Deleting an absent rating is `204`.

`GET api/me/rating-stats` (FR-E6) — computed in one grouped query:

```csharp
var rows = await db.ListEntries
    .Where(e => e.UserId == userId && e.Kind == ListKind.Watched && e.Rating != null)
    .GroupBy(e => e.Rating!.Value)
    .Select(g => new { Rating = g.Key, Count = g.Count() })
    .ToListAsync(ct);
```

Build `distribution` as a 10-element array (index 0 = rating 1), `count` as the
sum, `average` as the weighted mean in **half-star units** rounded to 2 decimals,
or `null` when `count == 0`. Do not divide by zero.

Stars, halves, and 0.5–5.0 display are the client's job. The server never sees a
fractional rating (FR-E2).

**Verify:**

```sh
curl -k -b c.txt -X PUT https://localhost:7173/api/films/278/rating \
     -H 'Content-Type: application/json' -d '{"rating":9}'    # 200, watched entry created
curl -k -b c.txt https://localhost:7173/api/lists/watched     # contains 278 with rating 9
curl -k -b c.txt -X PUT https://localhost:7173/api/films/278/rating \
     -H 'Content-Type: application/json' -d '{"rating":11}'   # 400
curl -k -b c.txt https://localhost:7173/api/me/rating-stats   # count 1, average 9, distribution[8]=1
curl -k -b c.txt -X DELETE https://localhost:7173/api/films/278/rating  # 204, still on watched
```

---

## Task 6 — Activity emission (FR-G7)

`Wopcorn.Server/Social/ActivityWriter.cs`, called from `ListService` and the
ratings controller **inside the same transaction as the mutation**.

| Trigger | Event |
|---|---|
| Add to Watched | `Watched` |
| Add to Watchlist | `AddedWatchlist` |
| Add to Queue | `AddedQueue` |
| Set/change rating | `Rated` with the new rating — **replace** any existing `Rated` event for that (user, film) rather than appending, so a re-rate moves up the feed instead of duplicating |
| Remove from a list | delete the corresponding event |
| Clear a rating | delete the `Rated` event |

`OccurredAt = DateTimeOffset.UtcNow`. An idempotent repeat-add emits nothing new.

Events are written for all users; be-04 decides who may read them. Nothing in
this plan exposes activity over HTTP.

**Verify:** add then remove a film from the watchlist and confirm `SELECT COUNT(*) FROM ActivityEvents` returns to its prior value.

---

## Done when

- [ ] All list, queue, and rating routes match the contract exactly
- [ ] Every mutating action verifies ownership through `CurrentUserId` only
- [ ] Queue positions are always contiguous from 0 after any operation
- [ ] Rating a film not on Watched adds it there
- [ ] Undoing any action removes its activity event
- [ ] A 200-entry list view issues a constant number of queries (log-verified, NFR-2)

## Hand off to be-04 with

The `ActivityWriter` API as built and confirmation that event retraction is
transactional.
