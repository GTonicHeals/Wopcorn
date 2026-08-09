using System.Collections.Concurrent;

namespace Wopcorn.Server.Tests;

/// <summary>
/// A signed-in user and a small, deliberately varied catalogue behind the fake
/// TMDB client. Shared by every be-03 suite.
///
/// Ids are ordered so that <c>id</c> order and title order agree, which keeps the
/// ordering assertions readable. One film (<see cref="Ember"/>) has no release
/// date, runtime, score or genres — it is what the nulls-last rule exists for,
/// and since plan 08 it has company: <see cref="Wander"/> is a series whose
/// <c>episode_run_time</c> is empty, which is the ordinary case rather than the
/// exceptional one.
/// </summary>
public sealed class ListWorld : IDisposable
{
    public const int Alien = 78;            // 1979 · 117 min · 8.1 · sci-fi, drama
    public const int BladeRunner = 79;      // 1982 · 117 min · 8.0 · sci-fi
    public const int Contact = 80;          // 1997 · 150 min · 7.4 · sci-fi, drama
    public const int Doubt = 81;            // 2008 · 104 min · 7.1 · drama
    public const int Ember = 82;            // nothing known but the title

    /// <summary>A series with a known episode length: 3 seasons, 24 episodes.</summary>
    public const int Beacon = 500;

    /// <summary>A series with an <b>empty</b> episode_run_time — runtime is null.</summary>
    public const int Wander = 501;

    public const int SciFi = 878;

    public static readonly int[] All = [Alien, BladeRunner, Contact, Doubt, Ember];

    private ListWorld(WopcornApiFactory factory, HttpClient client, FakeTmdbClient tmdb, Guid userId)
    {
        Factory = factory;
        Client = client;
        Tmdb = tmdb;
        UserId = userId;
    }

    public WopcornApiFactory Factory { get; }

    /// <summary>The owner's session — the user every assertion is about.</summary>
    public HttpClient Client { get; }

    public FakeTmdbClient Tmdb { get; }

    public Guid UserId { get; }

    /// <summary>The five films' keys, in the same order as <see cref="All"/>.</summary>
    public static string[] AllKeys => [.. All.Select(TestApi.Movie)];

    /// <summary>
    /// These films as title keys, for assertions against a response that now
    /// carries keys rather than ids — <c>Keys(Alien, Contact)</c> reads better than
    /// spelling <c>"movie-78"</c> out at every call site.
    /// </summary>
    public static string[] Keys(params int[] tmdbIds) => [.. tmdbIds.Select(TestApi.Movie)];

    public static async Task<ListWorld> CreateAsync(ConcurrentQueue<string>? sqlLog = null)
    {
        var tmdb = new FakeTmdbClient();
        tmdb.WithMovie(Alien, "Alien", "1979-05-25", 8.1, 117, SciFi, 18)
            .WithMovie(BladeRunner, "Blade Runner", "1982-06-25", 8.0, 117, SciFi)
            .WithMovie(Contact, "Contact", "1997-07-11", 7.4, 150, SciFi, 18)
            .WithMovie(Doubt, "Doubt", "2008-12-12", 7.1, 104, 18)
            .WithMovie(Ember, "Ember")
            .WithSeries(Beacon, "Beacon", "2011-04-17", 8.4,
                episodeRunTime: 50, seasonEpisodes: [10, 10, 4], genreIds: 18)
            .WithSeries(Wander, "Wander", "2008-01-20", 9.0,
                episodeRunTime: null, seasonEpisodes: [7, 13], genreIds: 18);

        var factory = new WopcornApiFactory { TmdbClient = tmdb, SqlLog = sqlLog };
        var client = factory.CreateSessionClient();
        var me = await client.RegisterAndReadAsync("owner@example.com", "password1", "Owner");

        return new ListWorld(factory, client, tmdb, Guid.Parse(me.Id));
    }

    /// <summary>A second signed-in user on the same instance.</summary>
    public async Task<(HttpClient Client, Guid UserId)> SignInAsync(string handle)
    {
        var client = Factory.CreateSessionClient();
        var user = await client.RegisterAndReadAsync($"{handle}@example.com", "password1", handle);
        return (client, Guid.Parse(user.Id));
    }

    /// <summary>
    /// Adds titles one at a time with a pause between, so <c>added</c> ordering is
    /// unambiguous — two adds inside one clock tick would fall through to the key
    /// tiebreak and make the assertion vacuous.
    /// </summary>
    public async Task AddPacedAsync(string list, params string[] keys)
    {
        foreach (var key in keys)
        {
            await Client.AddAndReadAsync(list, key);
            await Task.Delay(15);
        }
    }

    /// <summary>The film-key overload, which is what most of these suites want.</summary>
    public Task AddPacedAsync(string list, params int[] tmdbIds) =>
        AddPacedAsync(list, [.. tmdbIds.Select(TestApi.Movie)]);

    /// <summary>
    /// Asserts the <b>stored</b> queue is exactly these keys at positions
    /// <c>0..n-1</c>. Contiguity is checked on every call, so any operation that
    /// leaves a hole fails the first test that runs after it (FR-D1).
    /// </summary>
    public async Task AssertStoredQueueAsync(params string[] expectedKeys)
    {
        var rows = await Factory.QueuePositionsAsync(UserId);

        Assert.Equal(expectedKeys, rows.Select(r => r.Key));
        Assert.Equal(Enumerable.Range(0, expectedKeys.Length).Cast<int?>(), rows.Select(r => r.Position));
    }

    /// <inheritdoc cref="AssertStoredQueueAsync(string[])"/>
    public Task AssertStoredQueueAsync(params int[] expectedIds) =>
        AssertStoredQueueAsync([.. expectedIds.Select(TestApi.Movie)]);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
