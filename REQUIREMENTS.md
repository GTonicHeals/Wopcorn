# Wopcorn — Software Requirements

**Status:** Draft for planning handoff
**Date:** 2026-08-08

A single-page web application for tracking movies: what you've watched, what you
want to watch, what's up next, and what your friends think. All film data comes
from TMDB; all user data is owned by Wopcorn.

---

## 1. Glossary

These terms are used with precise meaning throughout. The naming follows TMDB /
Letterboxd convention.

| Term | Meaning |
|---|---|
| **Watched** | Films the user has already seen. Carries the rating. |
| **Watchlist** | Films the user wants to see, someday. Unordered. |
| **Queue** | Films to watch next. Ordered by hand. A short, prioritized list. |
| **Entry** | The relationship between one user and one film on one list. |
| **Friend** | Another user with a mutually accepted friendship. |
| **TMDB** | themoviedb.org, the upstream film catalog. |

Watched, Watchlist and Queue are **independent**. The same film may appear on
any combination of them at once.

---

## 2. Context and constraints

These are settled decisions, not open questions.

| Area | Decision |
|---|---|
| Deployment | Home LAN / small trusted group. Not public internet. |
| Scale | Tens of users, thousands of entries. Not a design driver. |
| Client | Vue 3 + TypeScript + Vite (already scaffolded) |
| Server | ASP.NET Core 10 (already scaffolded) |
| Data ownership | Wopcorn's own database. No writes to TMDB accounts. |
| Authentication | ASP.NET Core Identity, email + password, cookie-based |
| Social model | Mutual friend requests (both parties must consent) |
| Mobile | Installable PWA. Offline support **not** required. |
| Content type | Feature films only. No TV series, no people-as-first-class. |

**TMDB credentials** are stored in .NET user secrets on `Wopcorn.Server` under
`Tmdb:ReadAccessToken` and `Tmdb:ApiKey`. The read access token carries
`api_read` scope only — sufficient for all catalog reads, insufficient for
writing to a TMDB user account. Credentials must never be committed, and must
never be exposed to the browser.

---

## 3. Functional requirements

### 3.1 Accounts and authentication (FR-A)

- **FR-A1** — A visitor MUST be able to register an account with an email
  address, a password, and a display name.
- **FR-A2** — Display names MUST be unique, since they identify users when
  searching for friends.
- **FR-A3** — A registered user MUST be able to sign in and sign out.
- **FR-A4** — Sessions MUST persist across browser restarts, so a phone user is
  not asked to reauthenticate daily.
- **FR-A5** — Passwords MUST be stored using the ASP.NET Identity hasher.
  Password complexity rules MAY be relaxed from the framework defaults given the
  trusted-network deployment.
- **FR-A6** — Email confirmation and password reset are NOT required for the LAN
  deployment. The design MUST NOT preclude adding them later.
- **FR-A7** — A user MUST be able to edit their display name and set an avatar.
- **FR-A8** — All list, rating, and social endpoints MUST require authentication.

### 3.2 Film catalog (FR-B)

- **FR-B1** — Users MUST be able to search TMDB for films by title, with results
  showing poster, title, release year, and TMDB average score.
- **FR-B2** — Search MUST be usable one-handed on a phone and MUST return
  results incrementally as the user types, debounced to avoid a request per
  keystroke.
- **FR-B3** — A film detail view MUST show at minimum: poster, backdrop, title,
  release year, runtime, genres, synopsis, director, principal cast, and TMDB
  average score.
- **FR-B4** — The application SHOULD offer discovery beyond search — popular,
  top rated, and now playing — so a new user has something to browse
  immediately.
- **FR-B5** — All TMDB requests MUST be made server-side. The client MUST NOT
  hold or transmit TMDB credentials.
- **FR-B6** — Film metadata used for list and feed rendering MUST be cached
  locally, so displaying a list of N films does not require N upstream calls.
- **FR-B7** — Cached metadata MUST carry a fetch timestamp and MUST be
  refreshable when stale.
- **FR-B8** — TMDB does not publish a firm rate limit; the server SHOULD assume
  roughly 50 requests/second and MUST degrade gracefully — a TMDB outage or
  throttle MUST NOT break access to already-stored user data.
- **FR-B9** — TMDB attribution MUST be displayed as required by their terms of
  use.

### 3.3 Lists (FR-C)

- **FR-C1** — From search results and from the film detail view, a user MUST be
  able to add a film to Watched, Watchlist, or Queue, and remove it again.
- **FR-C2** — Adding to a list MUST be reachable in a single tap from a film
  card, without opening the detail view first.
- **FR-C3** — Any film's membership across all three lists MUST be visible at a
  glance wherever that film is displayed.
- **FR-C4** — Each list MUST have its own view, showing entry count and
  supporting sorting by date added, title, release year, runtime, TMDB score,
  and — for Watched — the user's own rating.
- **FR-C5** — List views MUST support filtering by genre and by release decade.
- **FR-C6** — Marking a film as Watched from the Queue or Watchlist SHOULD offer
  to remove it from that list in the same action.
- **FR-C7** — Each entry MUST record when it was added.

### 3.4 Queue ordering (FR-D)

- **FR-D1** — Queue entries MUST carry an explicit user-controlled position.
- **FR-D2** — A user MUST be able to reorder the queue by dragging, using touch
  on mobile and mouse on desktop.
- **FR-D3** — A user MUST be able to apply a sort preset (date added, title,
  runtime, TMDB score) to the queue. Applying a preset **rewrites** stored
  positions rather than acting as a temporary view.
- **FR-D4** — After a preset is applied, individual entries MUST remain
  hand-draggable from that new arrangement.
- **FR-D5** — Reordering MUST feel immediate: the UI updates optimistically and
  reconciles with the server afterward.

### 3.5 Ratings (FR-E)

- **FR-E1** — A user MUST be able to rate any film on their Watched list from
  0.5 to 5 stars in half-star increments — ten distinct values.
- **FR-E2** — Ratings MUST be stored as an integer 1–10 representing half-stars,
  and rendered as stars.
- **FR-E3** — Rating a film that is not yet on Watched MUST implicitly add it
  there.
- **FR-E4** — A user MUST be able to change or clear a rating at any time.
- **FR-E5** — The star control MUST be accurately operable by thumb on a phone,
  including reliable selection of half-star values.
- **FR-E6** — A user MUST be able to see their own rating distribution and
  average.

### 3.6 Friends (FR-F)

- **FR-F1** — A user MUST be able to find other users by display name.
- **FR-F2** — A user MUST be able to send a friend request; the recipient MUST
  accept before either party gains visibility of the other.
- **FR-F3** — A recipient MUST be able to accept or decline, and either party
  MUST be able to remove an existing friendship.
- **FR-F4** — Pending incoming requests MUST be surfaced with a visible in-app
  indicator.
- **FR-F5** — Friendship is symmetric: acceptance grants both users the same
  visibility of each other.
- **FR-F6** — Non-friends MUST NOT see a user's lists, ratings, or activity.
  Display name and avatar MAY be visible to support FR-F1.

### 3.7 Social surfaces (FR-G)

All three of the following are in scope.

- **FR-G1 (Profiles)** — A user MUST be able to view a friend's profile: their
  Watched, Watchlist, and Queue, their ratings, and their rating statistics.
- **FR-G2 (Feed)** — A user MUST have a reverse-chronological feed of friends'
  activity: ratings given, films marked watched, films added to lists.
- **FR-G3** — The feed MUST paginate and MUST NOT require loading full history
  to show recent items.
- **FR-G4 (Per-film context)** — Every film detail view MUST show which friends
  have seen it and what they rated it.
- **FR-G5 (Taste match)** — For each friend, the app MUST compute a taste-match
  score from films both users have rated, and MUST show how many films that
  score is based on — a match derived from three shared films is not
  trustworthy and must not be presented as if it were.
- **FR-G6** — Taste match MUST be hidden or explicitly qualified below a minimum
  overlap threshold.
- **FR-G7** — Activity MUST be generated as a side effect of user actions, and
  MUST disappear from friends' feeds if the underlying action is undone.

### 3.8 Mobile and PWA (FR-H)

- **FR-H1** — The application MUST be installable to a phone home screen: web
  app manifest, full icon set, standalone display mode.
- **FR-H2** — Layout MUST be designed mobile-first and MUST remain usable from
  320 px wide up to desktop.
- **FR-H3** — Primary navigation MUST be thumb-reachable on mobile.
- **FR-H4** — Interactive targets MUST be at least 44×44 px.
- **FR-H5** — The page body MUST NOT scroll horizontally at any viewport width.
- **FR-H6** — Poster images MUST be requested at a size appropriate to the
  viewport and MUST be lazily loaded in grids.
- **FR-H7** — A service worker MUST be registered to satisfy installability.
  Offline functionality is explicitly out of scope; the shell MAY be cached but
  user data need not be.
- **FR-H8** — Light and dark themes MUST both be supported, following the OS
  preference by default.

---

## 4. Non-functional requirements

- **NFR-1** — Search results SHOULD appear within 500 ms of typing stopping, on
  the LAN.
- **NFR-2** — A list view of 200 films MUST render without perceptible lag and
  MUST NOT issue per-film upstream requests.
- **NFR-3** — Every state-changing endpoint MUST verify that the authenticated
  user owns the affected data. Ownership MUST NOT be inferred from a
  client-supplied user identifier.
- **NFR-4** — Friend-scoped reads MUST verify an accepted friendship server-side
  on every request.
- **NFR-5** — The application MUST be served over HTTPS.
- **NFR-6** — The database schema MUST be managed by versioned migrations.
- **NFR-7** — The persistence layer MUST be swappable from SQLite to a
  server-grade database without application-level rewrites, should the
  deployment ever leave the LAN.
- **NFR-8** — Interactive elements MUST be keyboard accessible and
  screen-reader labelled. Drag-reordering MUST have a non-drag fallback.
- **NFR-9** — Text MUST meet WCAG AA contrast in both themes.
- **NFR-10** — Upstream TMDB failures MUST surface as actionable messages, never
  as a blank screen.

---

## 5. Out of scope

Explicitly excluded from this version, to keep planning bounded:

- TV series, episodes, and season tracking
- Writing to users' TMDB accounts (rating or watchlisting on TMDB itself)
- Offline use and offline write queuing
- Public internet deployment, email delivery, and account recovery
- Native mobile applications
- Streaming-provider availability, price tracking, or purchase links
- Comments, threaded discussion, or direct messaging between users
- Recommendation engine beyond the taste-match score in FR-G5
- Import from Letterboxd, IMDb, or CSV
- Administrative or moderation tooling

---

## 6. Open decisions

Four items remain unresolved. Each has a stated default so planning is not
blocked, but each is cheaper to settle before implementation than after.

- **OD-1 — Rewatches.** Does Watched hold one row per film, or one row per
  viewing? A per-viewing diary supports rewatch history and dated entries but
  changes the shape of the Watched entry and complicates "have I seen this".
  *Default: one row per film, with a single optional watch date.*

- **OD-2 — Review text.** Should a rating be able to carry written notes, and if
  so, are notes private or visible to friends in the feed?
  *Default: stars only, no text.*

- **OD-3 — Registration control.** Should registration be open to anyone who can
  reach the LAN address, or gated behind a shared invite code?
  *Default: open registration.*

- **OD-4 — Notification delivery.** Are in-app indicators (FR-F4) sufficient for
  friend requests and feed activity, or is push/email needed? Email would
  require an SMTP sender, which section 2 currently excludes.
  *Default: in-app only.*
