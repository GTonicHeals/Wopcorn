using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wopcorn.Server.Auth;
using Wopcorn.Server.Data;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Boots the real server in memory for integration tests.
///
/// The registered DbContext is swapped for a SQLite connection opened with
/// "DataSource=:memory:" and kept open for the lifetime of the factory, then
/// migrated. That exercises the real migrations (NFR-6) instead of the EF
/// in-memory provider, which does not enforce the unique indexes several
/// requirements depend on (FR-A2, FR-C1).
/// </summary>
public class WopcornApiFactory : WebApplicationFactory<Program>
{
    // Held open for the factory's lifetime: closing the last connection to a
    // SQLite ":memory:" database destroys it.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>
    /// Opt-in TMDB substitution. Tests that exercise the catalog construct the
    /// factory as <c>new WopcornApiFactory { TmdbClient = fake }</c>; every other
    /// test keeps the real <see cref="Wopcorn.Server.Tmdb.TmdbClient"/>, whose
    /// base URL points at tmdb.invalid — so an accidental live call still fails
    /// loudly rather than passing.
    /// </summary>
    public ITmdbClient? TmdbClient { get; init; }

    /// <summary>
    /// Opt-in EF command log, for the two claims that can only be checked against
    /// the generated SQL: that a list view costs a constant number of queries
    /// whatever its size (NFR-2), and that sorting really happens in the database
    /// rather than in memory (be-03 task 2).
    /// </summary>
    public ConcurrentQueue<string>? SqlLog { get; init; }

    /// <summary>
    /// Opt-in mailer substitution, so a test can read the reset link it is meant to
    /// follow. Without it the real mailer runs on its log-only path — see the
    /// blanked <c>Smtp:Host</c> above — and sends nothing either way.
    /// </summary>
    public FakeResetMailer? ResetMailer { get; init; }

    /// <summary>How many EF commands the log has recorded since it was last cleared.</summary>
    public int SqlCommandCount =>
        SqlLog?.Count(line => line.Contains("Executed DbCommand", StringComparison.Ordinal)) ?? 0;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Tests must never reach TMDB. be-02 reads these keys; pointing the base
        // URL at a black hole keeps an accidental live call from passing silently.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tmdb:BaseUrl"] = "https://tmdb.invalid/3/",
                ["Tmdb:ReadAccessToken"] = "test-token",
                ["Tmdb:ApiKey"] = "test-key",

                // The host boots in Development, which loads the developer's user
                // secrets — and those hold real SMTP credentials. Blanking the host
                // here is what stops a test run from mailing actual people: an
                // empty Smtp:Host puts the mailer on its log-only path.
                ["Smtp:Host"] = "",
                ["Smtp:UserName"] = "",
                ["Smtp:Password"] = "",
                ["Smtp:AppBaseUrl"] = "https://wopcorn.test",
            }));

        builder.ConfigureTestServices(services =>
        {
            // Drop everything Program.cs's AddDbContext registered — the context,
            // its options, and (EF 9+) the options-configuration callback that
            // still points at the on-disk wopcorn.db.
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(WopcornDbContext)
                                     || d.ServiceType == typeof(DbContextOptions)
                                     || d.ServiceType == typeof(DbContextOptions<WopcornDbContext>)
                                     || (d.ServiceType.FullName?.Contains(
                                         "DbContextOptionsConfiguration", StringComparison.Ordinal) ?? false))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            _connection.Open();
            services.AddDbContext<WopcornDbContext>(o =>
            {
                o.UseSqlite(_connection);

                if (SqlLog is { } log)
                {
                    o.LogTo(
                        line => log.Enqueue(line),
                        [DbLoggerCategory.Database.Command.Name],
                        LogLevel.Information);
                }
            });

            // Registered last, so it wins over the typed HttpClient registration.
            if (TmdbClient is { } fake)
            {
                services.AddSingleton(fake);
            }

            if (ResetMailer is { } mailer)
            {
                services.AddSingleton<IPasswordResetMailer>(mailer);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<WopcornDbContext>().Database.Migrate();

        return host;
    }

    /// <summary>
    /// The auth cookie is <c>SecurePolicy.Always</c> (NFR-5), so every test client
    /// must speak https or the cookie is silently dropped.
    /// </summary>
    private static WebApplicationFactoryClientOptions TestClientOptions(bool handleCookies) => new()
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = handleCookies,
        AllowAutoRedirect = false,
    };

    /// <summary>
    /// A client that keeps cookies, so a test can register once and stay signed in.
    /// </summary>
    public HttpClient CreateSessionClient() => CreateClient(TestClientOptions(handleCookies: true));

    /// <summary>A client that never carries a session cookie.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient(TestClientOptions(handleCookies: false));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
