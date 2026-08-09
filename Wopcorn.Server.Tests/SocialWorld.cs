using System.Collections.Concurrent;

namespace Wopcorn.Server.Tests;

/// <summary>One signed-in account, with the id every social assertion is about.</summary>
public sealed record Member(HttpClient Client, Guid Id, string DisplayName);

/// <summary>
/// Several accounts on one instance, over a catalogue big enough to seed a feed
/// that actually pages (be-04 task 6 needs sixty events).
///
/// Deliberately separate from <see cref="ListWorld"/>: the be-03 fixture is five
/// hand-tuned films chosen to exercise nulls-last ordering, and stretching it to
/// forty would make those assertions unreadable.
/// </summary>
public sealed class SocialWorld : IDisposable
{
    /// <summary>Films <c>1001..1040</c>, all known to the fake TMDB client.</summary>
    public const int FirstFilm = 1001;

    public const int FilmCount = 40;

    private readonly List<HttpClient> _clients = [];

    private SocialWorld(WopcornApiFactory factory, FakeTmdbClient tmdb)
    {
        Factory = factory;
        Tmdb = tmdb;
    }

    public WopcornApiFactory Factory { get; }

    public FakeTmdbClient Tmdb { get; }

    public static int Film(int index) => FirstFilm + index;

    public static IEnumerable<int> Films(int count) => Enumerable.Range(FirstFilm, count);

    /// <summary>The same films as title keys, which is what the API now takes.</summary>
    public static string Key(int index) => TestApi.Movie(Film(index));

    public static IEnumerable<string> Keys(int count) => Films(count).Select(TestApi.Movie);

    public static Task<SocialWorld> CreateAsync(ConcurrentQueue<string>? sqlLog = null)
    {
        var tmdb = new FakeTmdbClient();
        for (var i = 0; i < FilmCount; i++)
        {
            // Varied enough to sort by, uninteresting enough not to distract.
            tmdb.WithMovie(Film(i), $"Film {i:D2}", $"{1980 + i}-01-01", 5.0 + (i % 5), 90 + i, 878);
        }

        var factory = new WopcornApiFactory { TmdbClient = tmdb, SqlLog = sqlLog };
        return Task.FromResult(new SocialWorld(factory, tmdb));
    }

    /// <summary>Registers and signs in a new account.</summary>
    public async Task<Member> JoinAsync(string handle)
    {
        var client = Factory.CreateSessionClient();
        _clients.Add(client);

        var user = await client.RegisterAndReadAsync($"{handle}@example.com", "password1", handle);
        return new Member(client, Guid.Parse(user.Id), handle);
    }

    /// <summary>Registers two accounts and walks them through request → accept.</summary>
    public async Task<(Member A, Member B)> JoinFriendsAsync(string a, string b)
    {
        var first = await JoinAsync(a);
        var second = await JoinAsync(b);

        await TestApi.BefriendAsync(first.Client, first.Id, second.Client, second.Id);

        return (first, second);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        Factory.Dispose();
    }
}
