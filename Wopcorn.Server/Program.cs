using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wopcorn.Server.Api;
using Wopcorn.Server.Auth;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Lists;
using Wopcorn.Server.Social;
using Wopcorn.Server.Tmdb;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// TmdbUnavailableFilter is global: an upstream outage becomes 503
// tmdb_unavailable once, instead of a try/catch in every catalog action.
builder.Services.AddControllers(o =>
{
    o.Filters.Add<TmdbUnavailableFilter>();
    // Same idea for the social rules: RequireFriendshipAsync throws deep inside a
    // service and lands here as 403 forbidden (NFR-4).
    o.Filters.Add<ApiExceptionFilter>();
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<WopcornDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Wopcorn")));

// TMDB (be-02). Credentials come from user secrets and are read only inside
// TmdbClient; ITmdbClient is the seam the test harness substitutes.
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ITmdbClient, TmdbClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(8);
    if (!string.IsNullOrWhiteSpace(o.ReadAccessToken))
    {
        http.DefaultRequestHeaders.Authorization = new("Bearer", o.ReadAccessToken);
    }
});

builder.Services.AddScoped<GenreCatalogService>();
builder.Services.AddScoped<TitleCacheService>();
builder.Services.AddScoped<TitleMapper>();

// Streaming availability (09). The warmer is the app's only background service:
// availability is fetched when a title is opened, and nobody opens the titles
// already in their queue, so without it the Streaming filter matches almost
// nothing. It is deliberately timid — one upstream request a second, a bounded
// batch, and every pass wrapped in a catch.
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddHostedService<AvailabilityWarmer>();

// Lists, queue and ratings (be-03). ActivityWriter shares the request's
// DbContext, which is what makes an event and the mutation it reports atomic.
builder.Services.AddScoped<ActivityWriter>();
builder.Services.AddScoped<ListService>();
builder.Services.AddScoped<RatingStatsService>();
builder.Services.AddScoped<FavoritesService>();

// Friends, feed and taste match (be-04). TasteMatchInvalidator is a singleton
// because its change tokens have to outlive the request that cached an entry.
builder.Services.AddSingleton<TasteMatchInvalidator>();
builder.Services.AddScoped<FriendshipService>();
builder.Services.AddScoped<TasteMatchService>();
builder.Services.AddScoped<ProfileService>();

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
        // Must match WopcornDbContext.SchemaVersion — this is the half the store
        // reads, and it is what makes UserManager.SupportsUserPasskey true.
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<WopcornDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Passkeys are an *additional* sign-in method: every account still has a
// password, so nothing here can lock a user out of their own films.
//
// ServerDomain is deliberately left null. WebAuthn binds a credential to one
// relying-party id, and hardcoding it would mean a passkey registered on
// localhost:54429 in development is silently unusable on the LAN hostname in
// production. Null lets Identity derive it from the request host, so each origin
// gets credentials that work there.
builder.Services.Configure<IdentityPasskeyOptions>(o =>
{
    // Discoverable credentials are what make the usernameless "Sign in with a
    // passkey" button on /login possible — without one the browser has no way to
    // offer an account before it knows who is asking.
    o.ResidentKeyRequirement = "preferred";
    o.UserVerificationRequirement = "required";
});

// The reset link is mailed when SMTP is configured and logged when it is not, so
// the flow is exercisable on a laptop with no mail server in sight.
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.AddSingleton<IPasswordResetMailer, PasswordResetMailer>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "wopcorn.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;     // NFR-5
    o.ExpireTimeSpan = TimeSpan.FromDays(30);              // FR-A4
    o.SlidingExpiration = true;
    // API, not MVC: never redirect to a login page. The body is the ApiError
    // shape so the client parses one error format everywhere.
    o.Events.OnRedirectToLogin = ctx => WriteApiError(
        ctx.Response, 401, "unauthenticated", "You need to sign in.");
    o.Events.OnRedirectToAccessDenied = ctx => WriteApiError(
        ctx.Response, 403, "forbidden", "You do not have access to this.");
});

builder.Services.Configure<ApiBehaviorOptions>(o =>
    o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(
        new ApiError("validation_failed", "Some fields need attention.",
            ctx.ModelState.ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()))));

var app = builder.Build();

// One warning, at startup, if TMDB has no credentials: every catalog call will
// answer 503 tmdb_unavailable. The values themselves are never logged.
var tmdbOptions = app.Services.GetRequiredService<IOptions<TmdbOptions>>().Value;
if (string.IsNullOrWhiteSpace(tmdbOptions.ReadAccessToken) && string.IsNullOrWhiteSpace(tmdbOptions.ApiKey))
{
    app.Logger.LogWarning(
        "No TMDB credentials configured (Tmdb:ReadAccessToken or Tmdb:ApiKey). " +
        "Catalog endpoints will answer 503 until one is set in user secrets.");
}

// Avatars are written at runtime, so they are outside the MapStaticAssets
// build-time manifest. UseStaticFiles serves them from wwwroot.
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "avatars"));

app.UseDefaultFiles();
app.MapStaticAssets();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();

// Cookie auth would normally redirect to an HTML login page. Wopcorn is an API:
// answer with the status code and the ApiError body instead.
static Task WriteApiError(HttpResponse response, int status, string code, string message)
{
    response.StatusCode = status;
    return response.WriteAsJsonAsync(new ApiError(code, message));
}

// Top-level statements compile to an internal Program class. Making it public
// lets WebApplicationFactory<Program> in Wopcorn.Server.Tests boot the real host.
public partial class Program;
