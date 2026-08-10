# Wopcorn — Implementation Plans

`REQUIREMENTS.md` split into executable plans. Each plan names the model that
should execute it and is written at the altitude that model needs.

## Plan index

| Plan | Scope | Executor | Depends on |
|---|---|---|---|
| [`API-CONTRACT.md`](API-CONTRACT.md) | Shared HTTP contract — **the single source of truth for both sides** | — (reference) | — |
| [`00-testing.md`](00-testing.md) | Test infrastructure (**built**) and the test obligations each plan must satisfy | — (reference) | — |
| [`be-01-foundation.md`](be-01-foundation.md) | Scaffolding removal, `/api` convention, EF Core + SQLite, Identity, migrations | Opus 5 | — |
| [`be-02-catalog.md`](be-02-catalog.md) | TMDB client, local film cache, search / discovery / detail endpoints | Opus 5 | be-01 |
| [`be-03-lists-ratings.md`](be-03-lists-ratings.md) | Watched / Watchlist / Queue entries, ordering, ratings, stats | Opus 5 | be-01, be-02 |
| [`be-04-social.md`](be-04-social.md) | Friends, activity feed, per-film friend context, taste match | Opus 5 | be-01, be-03 |
| [`fe-05-shell.md`](fe-05-shell.md) | Design system, app shell, routing, auth screens, theming, PWA | Opus 5 | be-01 |
| [`fe-06-catalog-lists.md`](fe-06-catalog-lists.md) | Search, film detail, list views, queue drag-reorder, star control | Opus 5 | fe-05, be-02, be-03 |
| [`fe-07-social-pwa.md`](fe-07-social-pwa.md) | Friends UI, feed, profiles, taste match, mobile/a11y hardening | Opus 5 | fe-06, be-04 |
| [`08-series.md`](08-series.md) | TV series and seasons: re-keys the catalog, both tracks | Opus 5 | all of the above |
| [`09-availability.md`](09-availability.md) | Streaming availability, region + services, the Queue's "on my services" filter | Opus 5 | 08 |

## Why the plans read the way they do

**Both tracks are prescriptive.** The backend plans are prescriptive because the
work is well-specified CRUD — entity shapes, endpoint tables, and ownership
checks can all be written down completely before a line is typed, and there is
no cost to over-specifying them.

The frontend plans are prescriptive for a different reason. Design work normally
wants latitude — you decide the type scale and the card layout while looking at
the rendered result. Since the executor cannot work that way, **the design
decisions were made up front and written in as values**: measured contrast
ratios, exact hex tokens, the card's three-button action row, the star control's
pointer-to-value formula, the exact drag configuration. Where fe-05..fe-07 give a
number, it is the answer, not a starting point.

Two consequences worth knowing:

- The frontend dependencies (`vue-router`, `pinia`, `vuedraggable@^4.1.0`) are
  **already installed and verified against Vue 3.5.40**. `vuedraggable`'s npm
  `latest` tag still points at the Vue 2 build, so the `^4.1.0` constraint is
  load-bearing.
- The colour tokens in `fe-05` carry their measured WCAG ratios in comments. They
  were checked, not estimated; changing one means re-checking it (NFR-9).

The judgment that could not be front-loaded is the part that needs a real device:
whether half-star selection actually works under a thumb, and whether 200 cards
scroll smoothly. Both are called out as measure-then-decide steps.

## Sequencing

```
be-01 ──┬── be-02 ── be-03 ── be-04 ──┐
        │                             ├── 08 ── 09
        └── fe-05 ── fe-06 ── fe-07 ──┘
```

`08` is the only plan that changes a shipped contract rather than adding to it.
It waits on everything because it re-keys the table all seven of the others build
on.

`08` and `09` were both written after the app was already running. `09` is
strictly additive — it changes no existing route or column — but it depends on
`08` because a season's availability resolves through `ParentKey`, and it
re-opens one line of `REQUIREMENTS.md` §5 (provider availability; prices and
purchase links stay excluded).

Three things were decided differently from `09` as written, each recorded in
`API-CONTRACT.md`:

- **`availableOn` is loaded inside `TitleMapper.LoadUserContextAsync`**, not
  passed in by each action. The plan called for an audit of every call site; every
  one of them already supplies the viewer's id and the page's keys, which is
  exactly what availability needs, so folding it in leaves no call site to forget.
- **The region and services live on a new `GET /api/me`**, because the plan's
  "`GET /api/me` gains `region` and `providerIds`" had no route to gain them:
  `/api/auth/me` returns `UserSummary`, which also describes friends.
- **The Streaming filter is applied client-side against `availableOn`.** The
  server-side `service=` parameter is implemented and tested as specified, but the
  list views have always filtered locally, and the queue must keep its complete
  stored order in hand for `PUT /api/queue/order`. The two produce the same rows
  by construction — `availableOn` *is* the answer `service=` gives.

`fe-05` can start as soon as `be-01` exposes `/api/auth/*`. Everything else on
the frontend track waits for its backend counterpart. Backend plans are strictly
sequential — each one migrates the schema the next one builds on.

Both tracks run on Opus 5, but they execute independently — possibly in separate
sessions with no shared context — so [`API-CONTRACT.md`](API-CONTRACT.md) is the
only coordination point. Neither side may change a route, field, or status code
without editing that file first.

(The frontend track was originally assigned to Fable 5. It was moved to Opus 5
because the plans deliberately front-load every design decision as fixed values —
work that rewards literal, predictable execution, which is Opus 5's documented
strength, while heavily prescriptive plans are documented to *reduce* Fable 5's
output quality. Fable 5's premium buys judgment latitude these plans
intentionally removed.)

## Settled decisions (section 6 defaults, confirmed)

| ID | Decision |
|---|---|
| OD-1 | **One Watched row per film**, with a single optional `WatchedOn` date. No rewatch diary. |
| OD-2 | **Stars only.** No review text anywhere in the schema or API. |
| OD-3 | **Open registration.** No invite code. |
| OD-4 | **In-app indicators only.** No push, no SMTP. |

FR-A6 / NFR-7 still apply: none of these may be implemented in a way that
precludes adding the omitted feature later.

## Conventions that apply to every plan

- **Every API route is prefixed `/api`.** `vite.config.ts` proxies `^/api` once
  (be-01, task 3). No route may be added outside that prefix — it would 404 in
  development while working in production.
- **Ownership is never taken from the request body.** The acting user is
  `User.FindFirstValue(ClaimTypes.NameIdentifier)`, always (NFR-3).
- **TMDB ids are the film primary key** throughout the API. The client never
  sees an internal film row id.
- **Ratings are integers 1–10** on the wire and in the database. Halves and stars
  exist only in the UI (FR-E2).
- **Schema changes ship as EF Core migrations**, never as `EnsureCreated`
  (NFR-6).
- **A plan is not done until its tests in [`00-testing.md`](00-testing.md) pass.**
  The harness already exists; there is no setup cost to writing them.
- **No provider-specific SQL, no raw SQL, no SQLite-only types.** SQLite is a
  deployment choice, not an architectural one (NFR-7).
