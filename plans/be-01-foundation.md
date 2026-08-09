# be-01 — Foundation: scaffolding removal, persistence, Identity

**Executor:** Opus 5 · **Depends on:** nothing · **Blocks:** every other plan

Read [`API-CONTRACT.md`](API-CONTRACT.md) before starting. Implement exactly the
Auth section of it. Do not invent routes, fields, or status codes.

## Ground rules for this plan

- Work only inside `Wopcorn.Server/` plus the two named client files in task 3.
- Do not touch `wopcorn.client/src/**` — the frontend track owns it.
- Do not add NuGet packages beyond those listed in task 2.
- Match the version of `Microsoft.AspNetCore.OpenApi` already in the csproj
  (`10.0.10`) for every `Microsoft.*` package you add.
- After each task, run its **Verify** step. If it fails, fix it before moving on.

---

## Task 1 — Delete the template sample

Delete these files:

- `Wopcorn.Server/WeatherForecast.cs`
- `Wopcorn.Server/Controllers/WeatherForecastController.cs`
- `Wopcorn.Server/CHANGELOG.md`
- `Wopcorn.Server/Wopcorn.Server.http`

**Verify:** `dotnet build Wopcorn.slnx` succeeds and `grep -ri weatherforecast Wopcorn.Server` returns nothing.

---

## Task 2 — Add packages

Add to `Wopcorn.Server/Wopcorn.Server.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10" />
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.10" />
```

Install the CLI tool if absent: `dotnet tool install --global dotnet-ef`.

**Verify:** `dotnet restore Wopcorn.slnx` succeeds; `dotnet ef --version` prints a version.

---

## Task 3 — `/api` prefix and the Vite proxy

This is the step whose omission breaks development silently. Do it now, before
any endpoint exists.

1. In `wopcorn.client/vite.config.ts`, replace the whole `proxy` block with:

```ts
proxy: {
    '^/api': {
        target,
        secure: false
    }
},
```

2. In `wopcorn.client/src/components/`, leave every file alone. If
   `HelloWorld.vue` or `App.vue` calls `/weatherforecast`, leave that too — the
   frontend plan deletes it.

**Verify:** `grep -c weatherforecast wopcorn.client/vite.config.ts` returns `0`.

---

## Task 4 — Domain entities

Create `Wopcorn.Server/Data/Entities/` with one file per type.

```csharp
// AppUser.cs
public class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string? AvatarPath { get; set; }          // relative path under wwwroot/avatars
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
// Film.cs — the local TMDB cache (FR-B6, FR-B7). be-02 fills it.
public class Film
{
    public int TmdbId { get; set; }                  // PK, not generated
    public required string Title { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbVoteAverage { get; set; }
    public int? RuntimeMinutes { get; set; }
    public string? Overview { get; set; }
    public string? Director { get; set; }
    public string? CastJson { get; set; }            // serialized cast array, max 12
    public DateTimeOffset SummaryFetchedAt { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }
    public ICollection<FilmGenre> Genres { get; set; } = [];
}
```

```csharp
// Genre.cs
public class Genre { public int TmdbId { get; set; } public required string Name { get; set; } }

// FilmGenre.cs — join entity, composite key (FilmTmdbId, GenreTmdbId)
public class FilmGenre { public int FilmTmdbId { get; set; } public Film Film { get; set; } = null!;
                         public int GenreTmdbId { get; set; } public Genre Genre { get; set; } = null!; }
```

```csharp
// ListKind.cs
public enum ListKind { Watched = 1, Watchlist = 2, Queue = 3 }
```

```csharp
// ListEntry.cs — one row per (user, film, list). The three lists are independent.
public class ListEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int FilmTmdbId { get; set; }
    public Film Film { get; set; } = null!;
    public ListKind Kind { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public int? Position { get; set; }               // Queue only, 0-based contiguous
    public int? Rating { get; set; }                 // Watched only, 1..10 half-stars
    public DateOnly? WatchedOn { get; set; }         // Watched only, OD-1
}
```

```csharp
// Friendship.cs — ONE row per pair, not two. Ordered so UserAId < UserBId.
public class Friendship
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }  public AppUser UserA { get; set; } = null!;
    public Guid UserBId { get; set; }  public AppUser UserB { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

// FriendRequest.cs
public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }  public AppUser FromUser { get; set; } = null!;
    public Guid ToUserId { get; set; }    public AppUser ToUser { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
}
```

```csharp
// ActivityEvent.cs — be-04 reads it; be-03 writes it.
public enum ActivityKind { Rated = 1, Watched = 2, AddedWatchlist = 3, AddedQueue = 4 }

public class ActivityEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }  public AppUser User { get; set; } = null!;
    public int FilmTmdbId { get; set; }  public Film Film { get; set; } = null!;
    public ActivityKind Kind { get; set; }
    public int? Rating { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
```

Declare all of these now even though be-03 and be-04 populate them — one
migration for the whole schema is cheaper than four.

**Verify:** `dotnet build Wopcorn.slnx` succeeds.

---

## Task 5 — `WopcornDbContext`

Create `Wopcorn.Server/Data/WopcornDbContext.cs`:

```csharp
public class WopcornDbContext(DbContextOptions<WopcornDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Film> Films => Set<Film>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
}
```

In `OnModelCreating` (call `base.OnModelCreating(b)` first) configure:

| Entity | Configuration |
|---|---|
| `AppUser` | unique index on `DisplayName` (FR-A2) |
| `Film` | `TmdbId` is the key, `ValueGeneratedNever()` |
| `FilmGenre` | composite key `(FilmTmdbId, GenreTmdbId)` |
| `Genre` | `TmdbId` key, `ValueGeneratedNever()` |
| `ListEntry` | unique index `(UserId, FilmTmdbId, Kind)`; index `(UserId, Kind)`; delete-cascade from user |
| `Friendship` | unique index `(UserAId, UserBId)`; **both** FKs `DeleteBehavior.Restrict` (SQLite/SQL Server both reject multiple cascade paths otherwise) |
| `FriendRequest` | unique index `(FromUserId, ToUserId)`; both FKs `DeleteBehavior.Restrict` |
| `ActivityEvent` | index `(UserId, OccurredAt DESC)`; index `(OccurredAt DESC, Id)` for keyset paging |

Use only provider-agnostic fluent configuration — no `HasColumnType` with SQLite
type names, no raw SQL defaults (NFR-7).

**Verify:** `dotnet build Wopcorn.slnx` succeeds.

---

## Task 6 — Registration, connection string, migration

In `appsettings.json` add:

```json
"ConnectionStrings": { "Wopcorn": "Data Source=wopcorn.db" }
```

In `Program.cs`, before `builder.Build()`:

```csharp
builder.Services.AddDbContext<WopcornDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Wopcorn")));
```

Then create and apply the migration:

```sh
dotnet ef migrations add InitialSchema --project Wopcorn.Server
dotnet ef database update --project Wopcorn.Server
```

Add `wopcorn.db*` to a root `.gitignore` (create the file if absent; also ignore
`bin/`, `obj/`, `node_modules/`, `.vs/`, `dist/`).

**Verify:** `Wopcorn.Server/Migrations/` contains the migration; `dotnet ef database update` reports success; `wopcorn.db` exists.

---

## Task 7 — Identity with cookie authentication

Do **not** use `AddIdentityApiEndpoints` — it cannot carry `DisplayName` at
registration and defaults to bearer tokens. Wire it manually in `Program.cs`:

```csharp
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        // FR-A5: relaxed for the trusted LAN deployment.
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireDigit = false;
        o.SignIn.RequireConfirmedAccount = false;   // FR-A6
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<WopcornDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "wopcorn.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;     // NFR-5
    o.ExpireTimeSpan = TimeSpan.FromDays(30);              // FR-A4
    o.SlidingExpiration = true;
    // API, not MVC: never redirect to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
```

In the pipeline, add `app.UseAuthentication();` **before** the existing
`app.UseAuthorization();`.

> **Deployment note:** `SecurePolicy = Always` means the auth cookie is only
> ever sent over HTTPS. If the LAN deployment serves plain HTTP, login will
> *appear* to succeed but every subsequent request will be anonymous — no error,
> just a silent 401 loop. This is intentional (NFR-5): the deployment must serve
> HTTPS. Do not "fix" it by downgrading to `SameAsRequest`.

**Verify:** `dotnet build` succeeds and the app starts with `dotnet run --project Wopcorn.Server --launch-profile https`.

---

## Task 8 — Error shape and `ApiControllerBase`

Create `Wopcorn.Server/Api/ApiError.cs`:

```csharp
public record ApiError(string Code, string Message, IDictionary<string, string[]>? Errors = null);
```

Create `Wopcorn.Server/Api/ApiControllerBase.cs`:

```csharp
[ApiController]
[Authorize]
[Route("api")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);   // NFR-3

    protected IActionResult Problem(int status, string code, string message) =>
        StatusCode(status, new ApiError(code, message));
}
```

Every controller in every later plan derives from this and reads the acting user
**only** from `CurrentUserId`. A user id in a request body is never trusted.

Suppress the default `ModelState` filter so validation failures use our shape:

```csharp
builder.Services.Configure<ApiBehaviorOptions>(o =>
    o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(
        new ApiError("validation_failed", "Some fields need attention.",
            ctx.ModelState.ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()))));
```

**Verify:** build succeeds.

---

## Task 9 — `AuthController` and `MeController`

Create `Wopcorn.Server/Controllers/AuthController.cs`, route `api/auth`,
implementing exactly the Auth table in `API-CONTRACT.md`.

- `POST register` `[AllowAnonymous]` — validate `email` (EmailAddress),
  `password` (min 8), `displayName` (2–32 chars, trimmed). Reject a taken
  display name with `409 display_name_taken` **before** calling
  `CreateAsync`. On success call `signInManager.SignInAsync(user, isPersistent: true)`
  and return the `UserSummary`.
- `POST login` `[AllowAnonymous]` — `signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false)`. On failure return `401 unauthenticated` with the message
  "Email or password is incorrect." Do not reveal which.
- `POST logout` — `SignOutAsync()`, return `204`.
- `GET me` `[AllowAnonymous]` — return `200 UserSummary` when signed in, `401`
  otherwise. Anonymous so the client's boot check is not a console error.

Create `Wopcorn.Server/Controllers/MeController.cs`, route `api/me` (FR-A7):

- `PUT /` — rename; same display-name validation and `409` as registration; a
  no-op rename to the user's current name succeeds.
- `PUT /avatar` — accept `IFormFile file`, reject anything over 2 MB or whose
  content type is not `image/png`, `image/jpeg`, or `image/webp`. Save to
  `wwwroot/avatars/{userId}{ext}` (create the directory at startup if missing),
  store the relative path in `AvatarPath`, return `{ avatarUrl }`. Overwrite any
  previous avatar. Never use the client-supplied filename.
- `DELETE /avatar` — clear the field and delete the file.

Add a `UserSummary` mapper in `Wopcorn.Server/Api/Mapping.cs` that turns
`AppUser` into the contract DTO (`avatarUrl` = `"/avatars/…"` or `null`).
`app.UseStaticFiles()` is already implied by `MapStaticAssets()`; confirm avatars
are served, and add `app.UseStaticFiles()` explicitly if they are not.

**Verify** with the app running (`dotnet run --project Wopcorn.Server --launch-profile https`), against `https://localhost:7173`:

```sh
curl -k -c c.txt -X POST https://localhost:7173/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"a@b.c","password":"password1","displayName":"tester"}'      # 200
curl -k -b c.txt https://localhost:7173/api/auth/me                          # 200, tester
curl -k -X POST https://localhost:7173/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"d@e.f","password":"password1","displayName":"tester"}'      # 409 display_name_taken
curl -k https://localhost:7173/api/me -X PUT \
  -H 'Content-Type: application/json' -d '{"displayName":"x"}'              # 401
```

---

## Task 10 — Config endpoint

Create `Wopcorn.Server/Controllers/ConfigController.cs`, route `api/config`,
`[AllowAnonymous]`, returning the Config shape from the contract with:

```
imageBaseUrl:  "https://image.tmdb.org/t/p/"
posterSizes:   ["w92","w154","w185","w342","w500","w780","original"]
backdropSizes: ["w300","w780","w1280","original"]
profileSizes:  ["w45","w185","h632","original"]
attribution:   { text: "This product uses the TMDB API but is not endorsed or certified by TMDB.",
                 logoUrl: "/tmdb-logo.svg" }
```

Hardcode these (they are stable and this endpoint must work with TMDB down).
Place the TMDB logo at `Wopcorn.Server/wwwroot/tmdb-logo.svg`; if you cannot
obtain the asset, leave `logoUrl` pointing at the path and note it in the
handoff — the frontend plan will source it.

**Verify:** `curl -k https://localhost:7173/api/config` returns the object without authentication.

---

## Done when

- [ ] No `WeatherForecast` reference anywhere in the repo
- [ ] `vite.config.ts` proxies `^/api` and nothing else
- [ ] `InitialSchema` migration exists and applies to an empty database
- [ ] All ten Auth/Me/Config routes behave as the contract states
- [ ] `dotnet build Wopcorn.slnx` is warning-clean for nullable reference types

## Hand off to be-02 with

The migration name, whether `wwwroot/tmdb-logo.svg` was sourced, and any place
you deviated from this plan (with the reason).
