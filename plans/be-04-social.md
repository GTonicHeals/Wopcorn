# be-04 — Friends, feed, per-film context, taste match

**Executor:** Opus 5 · **Depends on:** be-01, be-03 · **Blocks:** fe-07

Implements FR-F1…FR-F6, FR-G1…FR-G7, NFR-4. Implement exactly the Friends and
Feed sections of [`API-CONTRACT.md`](API-CONTRACT.md).

## Ground rules

- **Every friend-scoped read re-verifies the friendship on that request** (NFR-4).
  No caching of "is a friend", no trusting a previously issued id.
- Non-friends may see only `displayName` and `avatarUrl` — never lists, ratings,
  counts, stats, or activity (FR-F6).
- Friendship is one row with `UserAId < UserBId` (be-01). Always normalise the
  pair before querying or inserting; never write two rows.
- No new NuGet packages.

---

## Task 1 — `FriendshipService`

`Wopcorn.Server/Social/FriendshipService.cs`.

```csharp
static (Guid A, Guid B) Pair(Guid x, Guid y) => x.CompareTo(y) < 0 ? (x, y) : (y, x);

Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken ct);
Task<Guid> RequireFriendshipAsync(Guid actor, Guid other, CancellationToken ct); // throws ForbiddenException
Task<FriendRequest> SendRequestAsync(Guid from, Guid to, CancellationToken ct);
Task<Friendship> AcceptAsync(Guid actor, Guid requestId, CancellationToken ct);
Task DeclineAsync(Guid actor, Guid requestId, CancellationToken ct);
Task RemoveAsync(Guid actor, Guid other, CancellationToken ct);
```

Rules:

- `SendRequestAsync` — `409 already_friends` if the pair is already friends;
  `409 request_pending` if a request exists in either direction; `400` if
  `from == to`. **If the reverse request already exists, do not create a second
  one** — return `409 request_pending` and let the caller accept the incoming
  one instead.
- `AcceptAsync` / `DeclineAsync` — only the **recipient** may act. Anyone else,
  including the sender, gets `403 forbidden`. Accept deletes the request row and
  inserts the normalised `Friendship` in one transaction (FR-F2, FR-F5).
- `RemoveAsync` — either party may remove (FR-F3). Deleting the row revokes
  visibility for both immediately; nothing else needs cleaning up because
  activity is filtered at read time.

Add `Wopcorn.Server/Api/ForbiddenException.cs` and map it in the existing
exception handler to `403 { code: "forbidden" }`.

**Verify:** A sends to B; A cannot accept (403); B accepts; A sending again returns `409 already_friends`; B removes; A can send again.

---

## Task 2 — User search and friends list

`Wopcorn.Server/Controllers/FriendsController.cs`.

**`GET api/users/search?q=`** (FR-F1) — case-insensitive prefix match on
`DisplayName`, excluding self, max 20 results. Return `UserSummary` plus
`relationship` ∈ `none` | `friends` | `request_sent` | `request_received`,
computed with one query against `Friendships` and one against `FriendRequests`
for the whole result set — not per row. Blank `q` returns `[]`.

**`GET api/friends`** — returns `{ friends, incoming, outgoing }` in one call so
the client can render the pending-request badge (FR-F4) from a single request.
Each `friends` row carries its `tasteMatch` (task 4).

**`POST api/friends/requests`** body `{ userId }` → `201`.
**`POST api/friends/requests/{id}/accept|decline`**, **`DELETE api/friends/{userId}`**
per the contract.

**Verify:** `GET /api/friends` for a user with one pending incoming request returns it under `incoming` and nothing under `friends`.

---

## Task 3 — Friend profiles (FR-G1)

**`GET api/friends/{userId}/profile`** — call `RequireFriendshipAsync` first,
then return `{ user, stats, counts, tasteMatch }`, where `stats` is the same
`RatingStats` shape as `GET /api/me/rating-stats` computed for the friend, and
`counts` is the entry count per list.

**`GET api/friends/{userId}/lists/{list}`** — `RequireFriendshipAsync`, then
reuse `ListService.GetAsync` with the friend's id and the same query parameters
as the owner's endpoint. The returned `FilmCard.lists` and `myRating` must
still describe **the requesting user**, not the friend — that is what makes
"my friend liked this, and it's on my watchlist" render in one pass. The
friend's rating rides on `ListEntry.rating`.

Extract the stats computation from be-03 into a shared
`RatingStatsService.ComputeAsync(Guid userId, …)` used by both endpoints rather
than duplicating the grouped query.

**Verify:** a non-friend requesting either route gets `403 forbidden`, not `404` or an empty list.

---

## Task 4 — Taste match (FR-G5, FR-G6)

`Wopcorn.Server/Social/TasteMatchService.cs`.

Definition — fix it here and do not vary it:

```
shared  = films both users have rated (Watched entries with Rating != null)
n       = |shared|
MAD     = (1/n) * Σ |ratingA(f) - ratingB(f)|        // half-star units, 1..10
score   = round(100 * (1 - MAD / 9))                  // 9 = max possible difference
```

- `MinimumOverlap = 5`. Below it, return `{ score, sharedCount, qualified: false }`
  — the score is still computed and returned, but `qualified: false` obliges the
  client to hide or qualify it (FR-G6). `n == 0` returns `score: null`.
- Compute for a batch of friends in **one** query: join the actor's rated entries
  against all friends' rated entries on `FilmTmdbId`, group by friend id, and
  aggregate. `GET /api/friends` with 20 friends must not issue 20 queries.
- Cache per (actor, friend) in `IMemoryCache` for 5 minutes; invalidate the
  actor's whole cache entry set on any rating write. This is deliberately
  one-sided: the *friend's* cached view of the pair may stay stale for up to the
  5-minute TTL after the actor rates something. That is accepted — do not build
  cross-user invalidation for it.

Never present a score without `sharedCount` in the same payload — the contract
makes them one object for that reason.

**Verify:** two accounts with 3 shared ratings → `qualified: false`; add three more shared ratings → `qualified: true` and the score matches a hand calculation.

---

## Task 5 — Per-film friend context (FR-G4)

Extend `GET api/films/{tmdbId}` from be-02: populate `friendsWatched` with the
requesting user's friends who have a Watched entry for that film, each with their
rating (`null` if unrated). One query:

```csharp
// friendIds resolved from Friendships for the current user
db.ListEntries
  .Where(e => friendIds.Contains(e.UserId) && e.FilmTmdbId == tmdbId && e.Kind == ListKind.Watched)
  .Select(e => new { e.User, e.Rating })
```

Order by rating descending, unrated last. This is the one place be-04 edits a
be-02 file — change only the `friendsWatched` assignment.

**Verify:** `GET /api/films/{id}` shows a friend who rated it, shows nothing for a non-friend who also rated it.

---

## Task 6 — Feed (FR-G2, FR-G3, FR-G7)

`Wopcorn.Server/Controllers/FeedController.cs`, route `api/feed`.

Query:

```csharp
db.ActivityEvents
  .Where(e => friendIds.Contains(e.UserId))
  .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
```

**Keyset pagination, never `Skip`** (FR-G3). The cursor is a base64url string
encoding `{OccurredAt:O}|{Id}`; the next page is
`WHERE (OccurredAt, Id) < (cursorTime, cursorId)` expressed as
`e.OccurredAt < t || (e.OccurredAt == t && e.Id.CompareTo(id) < 0)`. Take
`limit + 1` rows to decide whether `nextCursor` is non-null. Reject a malformed
cursor with `400 validation_failed`, not a 500.

Hydrate films through `FilmCacheService.GetManyAsync` for the whole page — one
database read, no upstream calls (FR-B6, NFR-2) — and decorate with the
requesting user's own membership via `LoadUserContextAsync`.

Own activity is excluded; the feed is friends only.

FR-G7 needs no work here: be-03 deletes events on undo, so they simply stop
appearing. Do not add tombstones or soft deletes.

**Verify:** with 60 events across two friends, page through with `limit=20` and confirm three pages with no duplicates, no gaps, `nextCursor: null` on the last; the generated SQL contains no `OFFSET`. Then unfriend one friend and confirm their items vanish from page 1.

---

## Done when

- [ ] Every `/api/friends/{userId}/…` route calls `RequireFriendshipAsync` first
- [ ] A non-friend can see only display name and avatar, on every surface
- [ ] `GET /api/friends` with N friends issues a constant number of queries
- [ ] Taste match always ships with `sharedCount` and `qualified`
- [ ] Feed pagination is keyset-based and duplicate-free
- [ ] Undone actions disappear from friends' feeds

## Hand off to fe-07 with

The exact taste-match formula and `MinimumOverlap` as implemented, and the cursor
encoding, so the client can be tested against real pagination boundaries.
