using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Wopcorn.Server.Tests;

/// <summary>
/// NFR-3: the acting user comes from the session cookie and nothing else.
///
/// Written as a loop over <b>every</b> mutating route rather than one worked
/// example, because this is the requirement a later refactor is most likely to
/// break — a single example would not notice a new endpoint that reads an owner
/// off the wire. Adding a route to be-03 means adding a row here.
/// </summary>
public class ListOwnershipTests
{
    /// <summary>Every route be-03 adds that writes: method, template, body.</summary>
    public static TheoryData<string, string, string?> MutatingRoutes => new()
    {
        { "PUT", "/api/lists/watched/{id}", null },
        { "PUT", "/api/lists/watchlist/{id}", null },
        { "PUT", "/api/lists/queue/{id}", null },
        {
            "PUT", "/api/lists/watched/{id}",
            """{"alsoRemoveFrom":["watchlist","queue"],"watchedOn":"2020-01-01"}"""
        },
        { "DELETE", "/api/lists/watched/{id}", null },
        { "DELETE", "/api/lists/watchlist/{id}", null },
        { "DELETE", "/api/lists/queue/{id}", null },
        { "PUT", "/api/titles/{id}/rating", """{"rating":1}""" },
        { "DELETE", "/api/titles/{id}/rating", null },
        { "PUT", "/api/queue/order", """{"keys":["movie-80","movie-79","movie-78"]}""" },
        { "POST", "/api/queue/sort", """{"preset":"title"}""" },
    };

    /// <summary>The read routes too, for the 401 sweep.</summary>
    public static TheoryData<string, string, string?> AllRoutes
    {
        get
        {
            var routes = new TheoryData<string, string, string?>
            {
                { "GET", "/api/lists/watched", null },
                { "GET", "/api/lists/watchlist", null },
                { "GET", "/api/lists/queue", null },
                { "GET", "/api/me/rating-stats", null },
            };

            foreach (var row in MutatingRoutes)
            {
                routes.Add((string)row[0], (string)row[1], (string?)row[2]);
            }

            return routes;
        }
    }

    [Theory]
    [MemberData(nameof(MutatingRoutes))]
    public async Task User_B_cannot_mutate_user_As_entries(string method, string template, string? body)
    {
        using var world = await ListWorld.CreateAsync();

        // A's state: a rated watched entry carrying a date, a watchlist entry, and
        // a three-item queue in a known order.
        await world.Client.AddToListAsync("watched", ListWorld.Alien, new { watchedOn = "2024-02-02" });
        await world.Client.RateAsync(ListWorld.Alien, 7);
        await world.Client.AddAndReadAsync("watchlist", ListWorld.Doubt);
        await world.AddPacedAsync("queue", ListWorld.Alien, ListWorld.BladeRunner, ListWorld.Contact);

        var before = await SnapshotAsync(world, world.UserId);

        var (intruder, intruderId) = await world.SignInAsync("intruder");
        using var _guard = intruder;

        // Aim the route at every film A owns. There is no user id anywhere in a
        // request, so the only way to reach A's rows would be a server-side bug.
        foreach (var tmdbId in ListWorld.All)
        {
            using var request = new HttpRequestMessage(
                new HttpMethod(method), template.Replace("{id}", TestApi.Movie(tmdbId)));

            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await intruder.SendAsync(request);

            // The route answers for the intruder's own (empty) data — never a 500,
            // and never an authorization bypass.
            Assert.True(
                (int)response.StatusCode < 500,
                $"{method} {template} answered {(int)response.StatusCode}");
        }

        Assert.NotEqual(world.UserId, intruderId);
        Assert.Equal(before, await SnapshotAsync(world, world.UserId));
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_be03_route_answers_401_without_a_session(
        string method, string template, string? body)
    {
        using var factory = new WopcornApiFactory { TmdbClient = new FakeTmdbClient() };
        using var client = factory.CreateAnonymousClient();

        using var request = new HttpRequestMessage(
            new HttpMethod(method), template.Replace("{id}", TestApi.Movie(ListWorld.Alien)));

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);      // not an HTML login redirect
        Assert.Equal("unauthenticated", (await response.ReadApiErrorAsync()).Code);
    }

    /// <summary>
    /// Everything about a user that be-03 can write, as one comparable string —
    /// entries with their timing, position, rating and watched date, plus the
    /// activity rows.
    /// </summary>
    private static Task<string> SnapshotAsync(ListWorld world, Guid userId) =>
        world.Factory.QueryAsync(async db =>
        {
            var entries = await db.ListEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.TitleKey)
                .Select(e => new { e.Kind, e.TitleKey, e.AddedAt, e.Position, e.Rating, e.WatchedOn })
                .ToListAsync();

            var events = await db.ActivityEvents
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Kind)
                .ThenBy(a => a.TitleKey)
                .Select(a => new { a.Kind, a.TitleKey, a.Rating, a.OccurredAt })
                .ToListAsync();

            var lines = entries
                .Select(e =>
                    $"{e.Kind}/{e.TitleKey}/{e.AddedAt:O}/{e.Position}/{e.Rating}/{e.WatchedOn}")
                .Concat(events.Select(a =>
                    $"event:{a.Kind}/{a.TitleKey}/{a.Rating}/{a.OccurredAt:O}"));

            return string.Join("\n", lines);
        });
}
