# 00 — Testing infrastructure and per-plan test obligations

**Status:** infrastructure is **built and passing**. What remains is the test
obligations each later plan must satisfy as it lands.

## What already exists

### Server — `Wopcorn.Server.Tests`

xUnit project, `net10.0`, in `Wopcorn.slnx`. Run with:

```sh
dotnet test Wopcorn.Server.Tests/Wopcorn.Server.Tests.csproj
```

| File | Purpose |
|---|---|
| `WopcornApiFactory.cs` | `WebApplicationFactory<Program>` booting the real host in memory. Forces `Tmdb:BaseUrl` to an unroutable host so no test can silently reach TMDB. `CreateSessionClient()` returns a cookie-retaining client. |
| `HostSmokeTests.cs` | Proves the harness works: the DI graph resolves and the pipeline answers without a 5xx. |

Two supporting changes were needed and are already in place:

- `Program.cs` ends with `public partial class Program;` — top-level statements
  otherwise compile to an internal class that `WebApplicationFactory<Program>`
  cannot reach.
- The test project passes `AdditionalProperties="BuildClient=false"` on its
  project reference, and `Wopcorn.Server.csproj` guards its esproj reference with
  `Condition="'$(BuildClient)' != 'false'"`. Without this, every `dotnet test`
  run triggers `npm install` and a Vite build.

### Client — Vitest

```sh
npm run test:unit     # single run
npm run test:watch    # watch mode
```

| File | Purpose |
|---|---|
| `vitest.config.ts` | jsdom environment, `@` alias, tests at `src/**/__tests__/**/*.spec.ts`. **Standalone by design** — `vite.config.ts` shells out to `dotnet dev-certs` at load time, and unit tests must not require the .NET SDK. |
| `tsconfig.vitest.json` | Test-only TS project, referenced from `tsconfig.json`. `tsconfig.app.json` already excludes `src/**/__tests__/*`, so production type-checking never depends on test types. |
| `src/__tests__/harness.spec.ts` | Mounts an inline component to prove jsdom + Vue + `@vue/test-utils` work. **Delete once real component tests exist** — it deliberately depends on no app code, since fe-05 replaces all of it. |

`@vitest/eslint-plugin` is wired into `eslint.config.ts` for test files only.

### Incidental fix

`package.json` paired `oxlint@~1.74.0` with `eslint-plugin-oxlint@~1.73.0`, whose
peer range is `~1.73.0`. `npm install` failed with `ERESOLVE` on a clean checkout
— and since the esproj runs `npm install` on build, so did any solution build
that touched the client. Both are now `~1.77.0`. Note that
`eslint-plugin-oxlint@1.74.0` was never published; the two packages must be kept
on the same minor.

---

## Testing strategy

**Integration over unit, on the server.** The requirements that matter here are
about HTTP behaviour and authorization — "a non-friend gets 403", "ownership is
never inferred from the request body", "the feed paginates without duplicates".
Those are properties of the pipeline, not of a class in isolation. Test through
`WopcornApiFactory` and real HTTP. Reserve plain unit tests for pure logic with
interesting arithmetic: the taste-match formula, rating statistics, queue
position compaction, cursor encoding.

**Real migrations, real SQLite.** When be-01 lands the DbContext, override
`ConfigureWebHost` to swap the registered context for a SQLite connection opened
with `DataSource=:memory:` and held open for the factory's lifetime, then run
`Database.Migrate()`. Do **not** use the EF in-memory provider: it does not
enforce unique indexes, and FR-A2 (unique display names) and the
`(UserId, TitleKey, Kind)` constraint are exactly the things worth testing.
Migrating also keeps NFR-6 honest — a broken migration fails the test run.

**Never reach TMDB.** `WopcornApiFactory` already points `Tmdb:BaseUrl` at
`tmdb.invalid`. be-02 must make `TmdbClient` injectable enough to substitute a
fake in tests; a test that depends on live TMDB is flaky by construction and also
can't run without secrets.

**Client tests are for logic, not layout.** Vitest earns its keep on the
debounce/race handling in search, `posterUrl` size selection, optimistic queue
reconciliation, and the star control's pointer-position → half-star mapping.
It does not earn its keep asserting on markup, which fe-05..fe-07 will churn.
Visual and mobile requirements (FR-H2..H5, NFR-9) are verified by hand against a
real device — no automated substitute is in scope.

---

## Test obligations per plan

Each plan is not done until its tests pass. These replace the manual `curl`
verifications for behaviour that is worth keeping; keep the `curl` steps for
one-off confirmation during development.

### be-01

- Register → `GET /api/auth/me` returns the user; a second registration with the
  same display name returns `409 display_name_taken` (proves the unique index,
  hence proves the migration).
- Login with a wrong password returns `401` and the same message as an unknown
  email — no user enumeration.
- Every non-anonymous route returns `401` without a cookie, and the body is the
  `ApiError` shape rather than an HTML login redirect.
- Avatar upload rejects a non-image content type and anything over 2 MB.
- `GET /api/config` answers anonymously.

### be-02

- A search calls TMDB once and populates `Titles`; the same search again serves
  from the database (assert on a call counter in the fake client).
- With the fake client throwing, `GET /api/titles/{key}` for a previously cached
  title returns `200` with `stale: true`; for an uncached one returns `503
  tmdb_unavailable`; `/api/genres` still answers.
- A blank `q` returns an empty result set with **zero** upstream calls.
- Unit: the summary/detail TTL boundary — one second either side of the TTL.

### be-03

- Adding twice does not change `addedAt`; removing an absent entry is `204`.
- Removing the middle of a three-item queue leaves positions `0,1` — assert on
  stored positions, not on response order.
- `PUT /api/queue/order` with a set that does not match the stored queue returns
  `409 queue_out_of_sync` **and** the authoritative order.
- `POST /api/queue/sort` rewrites stored positions and a subsequent hand reorder
  still works (FR-D3/FR-D4 in one test).
- Rating a title not on Watched creates the Watched entry; clearing the rating
  keeps it. Rating outside 1–10 is `400`.
- Unit: `RatingStats` over a known set — distribution buckets, average, and the
  `count == 0` case returning `null` rather than dividing by zero.
- **Ownership:** user A cannot mutate user B's entries through any route. Write
  this as a loop over every mutating endpoint, not one example — NFR-3 is the
  requirement most likely to be violated by a later refactor.
- Undoing any action removes its `ActivityEvent`.

### be-04

- The sender cannot accept their own request (`403`); the recipient can.
- A reverse request while one is pending returns `409 request_pending` and does
  not create a second row.
- Every `/api/friends/{userId}/…` route returns `403` for a non-friend — again as
  a loop over all such routes (NFR-4).
- Unit: taste match over a hand-computed set; `sharedCount` below the threshold
  yields `qualified: false`; zero overlap yields `score: null`.
- Feed: seed 60 events, page with `limit=20`, assert three pages, no duplicates,
  no gaps, `nextCursor: null` at the end. Assert the generated SQL contains no
  `OFFSET`. A malformed cursor is `400`, not `500`.
- Unfriending removes that user's items from page 1.

### 08 — series and seasons

The id collision is the headline: **movie 1396 and tv 1396 coexist** as separate
rows, separate list entries and separate ratings. Then:

- Key parsing rejects `tv-abc`, `movie-1-s2` and the empty string with `400`
  `validation_failed` — never `404`, and never an upstream call. Every
  key-taking route rejects the same way.
- Fetching a series costs **one** upstream call and leaves a season row per
  entry of its `seasons[]`; opening a season with nothing cached creates its
  series row first.
- A series whose `episode_run_time` is `[]` has a null runtime and does not
  throw; null runtimes sort **last** in both directions.
- The `type` filter narrows `entries` while `count` stays the unfiltered total;
  an unknown `type` value is ignored rather than rejected.
- A queue holding a film, a series and a season reorders across all three.
- A series and its seasons are independent **in both directions**: watching a
  season does not watch the series, and rating the series does not rate its
  seasons. `seasonProgress` reports the count and implies nothing.
- Taste match over a film and a series of the same TMDB id shares **zero**
  titles.
- `/api/genres` returns the union of the two upstream lists, and `mediaTypes`
  says which side each genre came from.

Client:

- `titleKey` round-trips every form including `-s0`, and rejects every
  near-miss spelling — the grammar is the one thing both tracks must agree on,
  so it is tested directly rather than through a component.
- `TitleCard` renders the right meta line per media type, chips series and
  seasons but not films, and shows season progress only on a series with at
  least one watched season.
- The titles store keys by the canonical string: a film and a series of the same
  id are two entries, and a toggle on a season does not mark its series.

### fe-05..fe-07

- `api/client.ts`: `ApiError` parsing, and that a 401 clears the auth store.
- Search: a slow response for query *n-1* arriving after query *n* must not
  overwrite the newer results.
- `posterUrl`: picks the smallest size covering the target width at DPR 1, 2, 3.
- Star control: pointer x → half-star value across the full width, including
  both edges; keyboard arrow steps.
- Queue: optimistic reorder followed by a `409` reconciles to the server order.

---

## Definition of done for the test track

- [ ] `dotnet test Wopcorn.Server.Tests/Wopcorn.Server.Tests.csproj` green
- [ ] `npm run test:unit` green
- [ ] `HostSmokeTests` and `harness.spec.ts` deleted once real suites replace them
- [ ] No test depends on network access, user secrets, or a built client
