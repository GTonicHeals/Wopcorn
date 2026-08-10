# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Wopcorn is a **title** tracking SPA — films, TV series and individual seasons,
all at the same grain: watched history, watchlist, an ordered queue, star
ratings, and mutual-friend social features, with everything pulled from TMDB. See
`REQUIREMENTS.md` for the full spec.

**All nine plans in `plans/` are implemented.** The Visual Studio template
scaffolding is gone. Server and client are feature-complete against
`REQUIREMENTS.md`, with 277 server tests and 256 client tests passing.

Passkey sign-in, password reset, and the profile screen with its favourites
showcase were added after the original seven plans and are not described by any
of them — `plans/API-CONTRACT.md` is their spec.

| Area | Where |
|---|---|
| Entities, `WopcornDbContext`, migrations | `Wopcorn.Server/Data/` |
| The title key (media type + TMDB id + season) | `Wopcorn.Server/Data/Entities/TitleKey.cs`, `wopcorn.client/src/lib/titleKey.ts` |
| Auth (cookie), avatars, config | `Controllers/{Auth,Me,Config}Controller.cs` |
| Passkeys, reset mail | `Wopcorn.Server/Auth/`, `Controllers/PasskeysController.cs`, `wopcorn.client/src/lib/webauthn.ts` |
| TMDB client and the local catalog cache | `Wopcorn.Server/Tmdb/`, `Wopcorn.Server/Catalog/` |
| Streaming availability, region and services | `Catalog/Availability{Service,Warmer}.cs`, `Controllers/AvailabilityController.cs`, `wopcorn.client/src/components/{WhereToWatch,ProviderBadges}.vue` |
| Lists, queue ordering, ratings | `Wopcorn.Server/Lists/`, `Controllers/{Lists,Queue,Ratings}Controller.cs` |
| Friends, feed, taste match | `Wopcorn.Server/Social/`, `Controllers/{Friends,Feed}Controller.cs` |
| Profile payload, favourites showcase | `Wopcorn.Server/Social/ProfileService.cs`, `Lists/FavoritesService.cs`, `Controllers/ProfileController.cs`, `wopcorn.client/src/views/ProfileView.vue` |
| Design tokens and shell | `wopcorn.client/src/assets/tokens.css`, `src/components/AppShell.vue` |
| API client and wire types | `wopcorn.client/src/api/` |
| Hosting script and operator's manual | `deploy/Host-Wopcorn.ps1`, `deploy/HOSTING.md` |

**`plans/API-CONTRACT.md` is the source of truth for every route, field, and
status code.** Neither side may change one without editing that file first.

### The title key

TMDB's film and TV ids are **separate namespaces and they collide** — 1396 is
*Mirror* (1975) as a movie and *Breaking Bad* as a series. So nothing anywhere
identifies a title by a bare TMDB id. The identifier is one canonical string,
used as the primary key, the wire identifier, and the URL segment:

```
movie-603          a film
tv-1396            a series
tv-1396-s2         season 2 of that series
```

Grammar: `^(movie|tv)-(\d+)(-s(\d+))?$`; `-s0` is TMDB's specials season and is
legal. `TitleKey` (server) and `src/lib/titleKey.ts` (client) are the **only**
places the format is known — nothing else concatenates or splits these strings.
A key that does not parse is `400 validation_failed`, never `404`.

`Title.Key` is the only writable identity on a catalog row: `MediaType`,
`TmdbId` and `SeasonNumber` are set from a key through `ApplyKey`, never the
reverse, so the parts cannot drift from the key.

**Nothing cascades between a series and its seasons.** They are independent
entries, exactly as the three lists are independent of each other: marking a
season watched does not touch the series, and rating a series does not rate its
seasons. `seasonProgress` (`3 / 5 seasons`) is what the UI renders instead — it
counts entries and implies nothing.

**A null runtime is ordinary, not an error.** TMDB's `episode_run_time` is an
array and is frequently empty (Breaking Bad returns `[]`), so most series have no
derivable runtime. The `runtime` sort puts nulls last in *both* directions, and
list totals sum only what is known — the Lists header deliberately understates
rather than inventing episode lengths.

### Streaming availability is region-scoped and per viewer

TMDB exposes `/watch/providers` for **`movie` and `tv` only** — there is no season
endpoint — so a season's availability resolves through `ParentKey`, the same way
its genres do. `AvailabilityService` owns the three tables; nothing else writes
them.

Four rules the code depends on:

- **Availability never fails a page.** A providers fetch that cannot reach TMDB
  returns the stale stored rows, or `fetchedAt: null` when there are none — never
  `503`. That is the opposite of `GET /api/titles/{key}`, which legitimately 503s.
- **"Not fetched" and "fetched, nothing here" are different answers.** A
  `TitleAvailability` row exists for every `(title, region)` we have *asked*
  about; zero `TitleOffer` rows beside it means we looked and nobody carries it.
  Without that distinction a title on no service in Belgium is re-fetched forever.
- **One payload answers for the whole world.** TMDB returns every region whatever
  you asked for, so every region is stored (~140 rows per title). Storing one is a
  guaranteed second request the moment anyone sets a different region.
- **`TitleAvailability.FetchedAt` carries `UtcInstantConverter`**, because the
  warmer orders by it. See "Sorting by an instant".

`AvailabilityWarmer` is the app's **only background service**: availability is
fetched when a title is opened, and nobody opens the titles already in their
queue. It sweeps titles on any user's Queue or Watchlist (never Watched) at one
request a second, every 15 minutes. `WopcornApiFactory` removes it by default —
like the blanked `Smtp:Host`, a test run must not have a background thread making
its own calls through the fake client.

`availableOn` on a card is **the viewer's own services, flatrate only**, and it is
loaded inside `TitleMapper.LoadUserContextAsync` — so every list, search, feed and
profile path gets it without a call site to forget, and a viewer with no services
configured costs zero queries against the offer table.

Two things about the Queue under **On my services**, both verified on screen:

- **The hero is promoted, not filtered.** "Up next" has to mean "up next among
  what you can watch", so with the filter on it becomes the first title in stored
  order that survives it. Display only — nothing is written and clearing the
  filter restores position 1. Row numerals stay the *real* stored positions
  (a filtered queue can read 4, 5, 6), because renumbering against a subset would
  make the queue's one piece of numbering lie.
- **Reordering is suspended while filtered.** `PUT /api/queue/order` takes the
  complete queue, so a drag in a filtered view could only submit a corrupted one;
  the grips and move buttons are removed and the board says why.

`QueueBoard.vue` declares its filter state **above** `heroKey`, which has an
immediate watcher — a `const` referenced before its declaration throws only at
runtime, and only once something evaluates it. `QueueBoard.spec.ts` mounts the
board partly to guard that.

Three things remain outstanding, all noted in `plans/` and in `README.md`:

- `Wopcorn.Server/wwwroot/tmdb-logo.svg` does not exist. It is a trademarked
  asset that has to be dropped in by hand; `TmdbAttribution.vue` renders the
  attribution text and hides the broken image until then (FR-B9).
- The device-dependent requirements (FR-H1..H5, NFR-8, NFR-9 in situ, NFR-2
  scroll performance) have never been checked on real hardware. The values are
  implemented as specified but unverified.
- **No completed WebAuthn ceremony has ever been run.** Every endpoint around it
  is tested, but registering and signing in with a real passkey needs a real
  browser and authenticator, which no test here can stand in for.

### Version control

This **is** a git repository, pushed to `origin`
(`github.com/GTonicHeals/Wopcorn`). **The default branch is `master`, not
`main`** — target it for branches and PRs.

`.gitignore` covers build output (`bin/`, `obj/`, `dist/`), `node_modules/`,
`.vs/`, the local database (`wopcorn.db*`, which also catches the
`.bak-before-*` copies beside it), and `deploy/wopcorn.host.json` — the one
deploy file that holds secrets in plain text. Nothing else in `deploy/` is
secret.

## Commands

Client (`wopcorn.client/`):

```sh
npm install
npm run dev          # Vite dev server on https://localhost:54429
npm run build        # type-check + production build, in parallel
npm run type-check   # vue-tsc only
npm run lint         # oxlint --fix, then eslint --fix (both mutate files)
```

Server (repo root):

```sh
dotnet build Wopcorn.slnx
dotnet run --project Wopcorn.Server --launch-profile https
dotnet user-secrets list --project Wopcorn.Server

# Nothing migrates the dev database at startup — only the test factory does.
# After pulling a new migration, apply it by hand or every query against the new
# table fails at runtime with "no such table".
dotnet ef database update --project Wopcorn.Server --context WopcornDbContext -- -p:BuildClient=false
```

**Running the server is normally enough.** `launchSettings.json` registers
`Microsoft.AspNetCore.SpaProxy` as a hosting startup assembly, which launches
`npm run dev` for you. Only start Vite by hand when working on the client
in isolation.

A running server holds `bin/Debug/net10.0/Wopcorn.Server.exe` open, so any
`dotnet build`, `dotnet test`, or `dotnet ef` command fails with MSB3027 until it
is stopped. That error means "the app is still running", not "the code is
broken".

### Tests

```sh
dotnet test Wopcorn.Server.Tests/Wopcorn.Server.Tests.csproj   # xUnit + WebApplicationFactory
npm run test:unit                                              # Vitest, from wopcorn.client/
```

Server tests boot the real host through `WopcornApiFactory`, which points
`Tmdb:BaseUrl` at an unroutable host — no test may reach TMDB. The test project
passes `BuildClient=false` so `dotnet test` skips the esproj reference and does
not run `npm install`; `Program.cs` ends with `public partial class Program;`
purely so `WebApplicationFactory<Program>` can see it.

`vitest.config.ts` is intentionally standalone rather than merged with
`vite.config.ts`, which shells out to `dotnet dev-certs` at load time. Test files
live at `src/**/__tests__/**/*.spec.ts` and are excluded from
`tsconfig.app.json`, so the *production* type-check never depends on test-only
types.

They are still type-checked, though: `tsconfig.json` also references
`tsconfig.vitest.json`, and `type-check` is `vue-tsc --build`, which builds every
referenced project. A type error in a `.spec.ts` fails `npm run type-check` and
`npm run build` just like one in `src/`. `noUncheckedIndexedAccess` is on, so
`array[0]` is `T | undefined` in tests as well — indexing a `mock.calls` entry or
an `allowCredentials` list needs a guard.

Server tests are integration tests over real HTTP against a SQLite
`DataSource=:memory:` database that `WopcornApiFactory` migrates — not the EF
in-memory provider, which does not enforce the unique indexes several tests
depend on. Substitute a fake TMDB client with
`new WopcornApiFactory { TmdbClient = fake }`; `FakeTmdbClient` has per-method
call counters (films *and* TV) and a `Throw` flag for outage tests, and the
factory exposes an opt-in `SqlLog` for asserting on generated SQL (the feed's
no-`OFFSET` guarantee). `SocialWorld` and `ListWorld` are the shared fixtures;
`ListWorld` carries two series on purpose, one with a known episode length and
one whose `episode_run_time` is empty.

`AvailabilityWorld` is the plan-09 fixture: a film carried by different services
in two regions, a film TMDB has provider data for that nobody carries, and a
series whose seasons resolve to it. `FakeTmdbClient.WithProviders` registers one
region at a time; asking it for a **season's** providers throws, exactly as the
real client does.

`TestApi.Movie/Series/Season` spell title keys, and the film-id overloads on the
list helpers exist so suites that predate series — and are about behaviour all
three media types share — stay readable. Anything actually *about* media types
spells the key out.

**The test host boots in Development, which loads the developer's user secrets —
including real SMTP credentials.** `WopcornApiFactory` blanks `Smtp:Host` in
configuration for exactly this reason: an empty host puts the mailer on its
log-only path, so a test run cannot mail anyone. Do not remove that override. To
read the link a test is meant to follow, pass
`new WopcornApiFactory { ResetMailer = new FakeResetMailer() }` and call
`SingleLinkFor(email)`.

Client tests cover logic, not layout — the search race guard, `posterUrl` size
selection, the star control's pointer mapping, optimistic queue reconciliation,
store transitions, and the title-key grammar. `TitleCard.spec.ts` is the one
component test that asserts on rendered text, because the per-media-type meta
line *is* the behaviour. See `plans/00-testing.md` for the strategy and the
per-plan test obligations.

## Architecture

### The catalog is one table

`Titles` holds films, series and seasons at one grain, keyed by the title key,
with `ParentKey` pointing a season at its series. `ListEntries`, `ActivityEvents`
and `TitleGenre` all foreign-key to `Titles.Key` — one `TEXT` column each, which
is why the key is a single string rather than a composite
`(MediaType, TmdbId, SeasonNumber)`.

Fetching a **series** detail also writes a summary row per season from the
`seasons[]` array TMDB already returns, so opening a series costs one upstream
request rather than one per season. Season *details* are fetched only when a
season screen is opened. A season may never exist without its series row.

`Genres` mirrors the **union** of `/genre/movie/list` and `/genre/tv/list`, with
`InMovies`/`InTv` recording which side each came from. The ids overlap where the
names match (Drama 18), so `GenreCatalogService.EnsureAsync` merges against
`db.Genres.Local` as well as the database — otherwise the TV pass adds a second
entity with a key the movie pass already tracked, and EF refuses it.

### One profile, two viewers

`GET /api/me/profile` and `GET /api/friends/{userId}/profile` return the **same**
`ProfileDto`, built by one `ProfileService`, and `ProfileView.vue` renders both.
Only two fields differ: `isSelf`, and `tasteMatch`, which is null on your own
profile. A profile that looked different to its owner would be one its owner
could not judge.

The favourites showcase is a small table of its own, **not** a fourth
`ListKind` — the three lists are a closed set with membership, positions, watched
dates and ratings hanging off them, and a favourite is an ordered reference and
nothing else. `PUT /api/me/favorites` replaces the whole showcase, like
`PUT /api/queue/order` replaces the whole queue, so add, remove and reorder are
one write. Position 0 is the title the profile takes its marquee wash from, which
is how the order is made visible without numerals — those belong to the queue.

`RuntimeOnRecord` splits the watched list's runtime into `minutes` (what is
known) and `unknownTitles` (what is not), because a series with an empty
`episode_run_time` contributes nothing. That split is what lets the profile say
"at least 33h 48m" rather than passing an understatement off as a total.

### The dev-time proxy handshake

Understanding how a request reaches the server in development requires three
files together — `launchSettings.json`, `Wopcorn.Server.csproj`, and
`vite.config.ts`:

1. The server runs on `https://localhost:7173` (and `http://localhost:5159`).
2. SpaProxy starts Vite on `https://localhost:54429`; the browser talks to Vite,
   not to Kestrel.
3. `vite.config.ts` proxies a **hardcoded allowlist of path prefixes** back to
   the server — now just `^/api`.

**Consequence:** every API route must live under `/api`. A route outside that
prefix 404s in development while working fine in production, which is why the
single `^/api` proxy replaced the per-route allowlist. Do not add a second
prefix; add the route under `/api` instead.

`vite.config.ts` also generates and reads an HTTPS certificate via
`dotnet dev-certs` at config-load time, so Vite cannot start without the .NET SDK
present.

### Production serving

`Program.cs` uses `UseDefaultFiles()` + `MapStaticAssets()` +
`MapFallbackToFile("/index.html")`. The Vite build output is served as static
files from the server, and any unmatched path returns `index.html` for
client-side routing. There is no Vite proxy in production — client and API are
same-origin, which is what makes cookie authentication straightforward.

### The deployed host

`deploy/Host-Wopcorn.ps1` publishes, migrates, starts and fronts the app on a
Windows machine; `deploy/HOSTING.md` is the operator's manual. Kestrel binds
`127.0.0.1` only and `tailscale serve` terminates HTTPS with a real certificate
for the tailnet name — nothing is exposed to the internet.

Three consequences that change how server code must be written:

- **Production configuration arrives as environment variables, never as user
  secrets.** .NET user secrets load only in Development, so a production host
  cannot read them. `deploy/wopcorn.host.json` holds the values and the script
  projects them onto the process as `Tmdb__ReadAccessToken`, `Smtp__Password`
  and friends. Anything new that needs configuring has to work through that
  path too.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is load-bearing.** Tailscale
  terminates TLS and forwards plain HTTP to Kestrel; without it the app builds
  `http://` origins, which breaks password-reset links and fails **every**
  WebAuthn ceremony. Verified both ways on this codebase.
- **Nothing migrates the database at startup, in production either.** `deploy`
  runs `dotnet ef database update`; `start` does not. A missing migration
  surfaces as `no such table` at query time.

Avatars live outside the published folder (`app\wwwroot\avatars` is a junction
to `C:\ProgramData\Wopcorn\avatars`), because every deploy overwrites `app\`.
Writing user uploads anywhere under the web root would lose them.

### Sorting by an instant

SQLite's EF provider throws `NotSupportedException` on `ORDER BY` over a
`DateTimeOffset`. Any column you intend to sort or paginate on must therefore
carry `HasConversion<UtcInstantConverter>()` (`Wopcorn.Server/Data/`), which
stores UTC ticks as a `long` — sortable on every provider, full precision, and
provider-agnostic per NFR-7. Already applied to `ListEntry.AddedAt`,
`ActivityEvent.OccurredAt`, `Friendship.CreatedAt`, and `FriendRequest.SentAt`.
Adding a new sortable timestamp means applying the converter *and* shipping a
migration; the failure otherwise appears only at query time, not at build time.

### TMDB access

TMDB credentials live in .NET user secrets on `Wopcorn.Server` under
`Tmdb:ReadAccessToken` (v4 bearer, `api_read` scope) and `Tmdb:ApiKey` (v3).

Every TMDB call must be made server-side and exposed to the client through a
Wopcorn endpoint. The credentials must never reach the browser bundle, and the
`api_read` scope cannot write to TMDB user accounts in any case — user data is
owned by Wopcorn's own database.

`REQUIREMENTS.md` FR-B6 calls for caching title metadata locally: rendering a
list of N titles must not produce N upstream requests.

### Passkeys ride on Identity, and need a schema version

.NET 10's Identity does WebAuthn itself — `SignInManager.MakePasskey*OptionsAsync`,
`PerformPasskeyAttestationAsync`/`PerformPasskeyAssertionAsync`, and
`UserManager.{AddOrUpdate,Get,Remove}PasskeyAsync`. There is no third-party
library and there must not be one. `IPasskeyHandler<AppUser>` is registered for
free by `AddIdentityCore().AddSignInManager()`.

**Two settings have to agree, and neither fails at build time:**

- `WopcornDbContext.SchemaVersion` → `IdentitySchemaVersions.Version3`, which is
  what puts `AspNetUserPasskeys` in the EF model. Below that the entity is simply
  absent and `dotnet ef migrations add` produces an *empty* migration — the
  symptom is a successful, silent no-op.
- `IdentityOptions.Stores.SchemaVersion` in `Program.cs`, which is the half the
  store reads and what makes `UserManager.SupportsUserPasskey` true.

`IdentityPasskeyOptions.ServerDomain` is deliberately left null. WebAuthn binds a
credential to one relying-party id, so hardcoding it would make a passkey
registered on `localhost:54429` silently unusable on the LAN hostname. Null lets
Identity derive it per request.

Both options endpoints set a short-lived challenge-state cookie that the matching
follow-up call must echo back — the two calls are one exchange, which is why the
client's `credentials: 'include'` is load-bearing here.

Identity *returns* a failed result for a rejected credential but *throws* when the
exchange never made sense (unparseable JSON, or a POST that skipped the options
call). Both controllers catch `PasskeyException`/`JsonException`/
`InvalidOperationException` around those calls; without it an anonymous endpoint
500s on a garbage body.

`wopcorn.client/src/lib/webauthn.ts` is the base64url ↔ `ArrayBuffer` translation.
It prefers the browser's own `parseCreationOptionsFromJSON`/`toJSON` and keeps a
manual fallback, which is the path the unit tests exercise (jsdom has neither).

### Password reset mail

`Smtp:*` in user secrets, alongside the TMDB keys — in production the same
values arrive as `Smtp__*` environment variables instead. `Smtp:Host` is the
switch:
set, mail goes out over SMTP; empty, `PasswordResetMailer` logs the link at
Information instead, so the flow works on a laptop with no mail server.
`Smtp:AppBaseUrl` sets the origin the link points at, which matters in
development because the browser is on Vite's port and the request reaches Kestrel
on another.

`POST /api/auth/forgot-password` answers `202` for **every** input, including
unknown and malformed addresses, and `IPasswordResetMailer.SendAsync` never
throws — a mail failure must not become a 500 that confirms an account exists.

## Client conventions

- `@` is aliased to `wopcorn.client/src`.
- `.editorconfig` applies to client source only: 2-space indent, LF, UTF-8,
  100-column lines, final newline, no trailing whitespace.
- Two linters run in sequence. `oxlint` runs first for speed;
  `eslint-plugin-oxlint` then disables the ESLint rules oxlint already covers,
  reading `.oxlintrc.json` to do so. Rule changes may need to be made in both
  places.
- `npm run lint` writes to files (`--fix` on both). Use `npx eslint .` for a
  read-only check.
- **The accent colour means the signed-in user's own state** — their rating,
  their list membership, their season progress, their chosen services and the
  filters they have applied, the active nav item. It is never decorative.
  Position numerals appear only in the queue.
- **Provider logos are the one thing not drawn from `tokens.css`.** They are
  third-party brand marks in their own colours, which is why they are small,
  boxed with a neutral border, and never adjacent to the accent — a Netflix red
  beside the gold is two competing signals. An empty `availableOn` renders
  **nothing**: it cannot distinguish "unknown" from "on none of yours".
- **The type chip labels series and seasons, never films.** The default needs no
  label, and a chip on every card is noise that hides the two that matter. It is
  neutral, not accent — what kind of thing something is is not user state.
- Colour tokens in `tokens.css` carry their measured WCAG ratios in comments.
  They were checked, not estimated; changing one means re-checking it (NFR-9).
- The Inter and Fraunces `@font-face` blocks in `tokens.css` are commented out —
  the woff2 files were never shipped, so everything renders through the fallback
  stacks. Dropping the files into `src/assets/fonts/` and uncommenting is the
  only step needed.
- No UI library, CSS framework, or icon package. Icons are hand-written 24×24
  SFCs in `src/components/icons/` using `stroke="currentColor"`.
