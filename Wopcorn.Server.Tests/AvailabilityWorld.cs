using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>
/// A signed-in user and a catalogue built for the availability questions: a film
/// carried in two regions by different services, a film TMDB has provider data for
/// but nobody carries, and a series whose seasons resolve to it.
/// </summary>
public sealed class AvailabilityWorld : IDisposable
{
    /// <summary>On Netflix in GB, on Apple TV in BE, and rentable from Prime in GB.</summary>
    public const int Sicario = 273481;

    /// <summary>TMDB knows about it and no service anywhere carries it.</summary>
    public const int Nowhere = 260513;

    private AvailabilityWorld(
        WopcornApiFactory factory, HttpClient client, FakeTmdbClient tmdb, Guid userId)
    {
        Factory = factory;
        Client = client;
        Tmdb = tmdb;
        UserId = userId;
    }

    public WopcornApiFactory Factory { get; }

    public HttpClient Client { get; }

    public FakeTmdbClient Tmdb { get; }

    public Guid UserId { get; }

    public static async Task<AvailabilityWorld> CreateAsync(ConcurrentQueue<string>? sqlLog = null)
    {
        var tmdb = new FakeTmdbClient();

        tmdb.WithMovie(Sicario, "Sicario", "2015-09-18", 7.6, 121, 18)
            .WithMovie(Nowhere, "Nowhere", "2019-02-01", 6.0, 95, 18)
            .WithSeries(FakeTmdbClient.CollisionId, "Breaking Bad", "2008-01-20", 8.9,
                episodeRunTime: null, seasonEpisodes: [7, 13, 13], genreIds: 18);

        tmdb.WithProviders(MediaType.Movie, Sicario, "GB",
                flatrate: [FakeTmdbClient.NetflixId], rent: [FakeTmdbClient.PrimeVideoId])
            .WithProviders(MediaType.Movie, Sicario, "BE",
                flatrate: [FakeTmdbClient.AppleTvId])
            .WithNoProviders(MediaType.Movie, Nowhere)
            .WithProviders(MediaType.Series, FakeTmdbClient.CollisionId, "GB",
                flatrate: [FakeTmdbClient.NetflixId]);

        var factory = new WopcornApiFactory { TmdbClient = tmdb, SqlLog = sqlLog };
        var client = factory.CreateSessionClient();
        var me = await client.RegisterAndReadAsync("viewer@example.com", "password1", "Viewer");

        return new AvailabilityWorld(factory, client, tmdb, Guid.Parse(me.Id));
    }

    public async Task<(HttpClient Client, Guid UserId)> SignInAsync(string handle)
    {
        var client = Factory.CreateSessionClient();
        var user = await client.RegisterAndReadAsync($"{handle}@example.com", "password1", handle);
        return (client, Guid.Parse(user.Id));
    }

    /// <summary>
    /// Ages every stored answer for one title past the TTL, so the next read has to
    /// go upstream. Faster and more precise than waiting 24 hours.
    /// </summary>
    public Task ExpireAsync(string titleKey) =>
        Factory.QueryAsync(async db =>
        {
            var stale = DateTimeOffset.UtcNow - AvailabilityService.AvailabilityTtl
                        - TimeSpan.FromMinutes(1);

            var rows = await db.TitleAvailability.Where(a => a.TitleKey == titleKey).ToListAsync();
            foreach (var row in rows)
            {
                row.FetchedAt = stale;
            }

            return await db.SaveChangesAsync();
        });

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
