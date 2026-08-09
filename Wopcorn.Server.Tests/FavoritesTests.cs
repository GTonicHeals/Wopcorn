using System.Net;

namespace Wopcorn.Server.Tests;

/// <summary>
/// The favourites showcase (API-CONTRACT.md, "Profile and favourites").
///
/// The showcase is written as a whole, like the queue's order, so every test here
/// is about one of three things: the write replaced what was there, the order came
/// back the way it went in, or a refusal left the previous showcase untouched.
/// </summary>
public class FavoritesTests
{
    [Fact]
    public async Task Favorites_start_empty()
    {
        using var world = await ListWorld.CreateAsync();

        Assert.Empty(await world.Client.GetFavoritesAsync());
    }

    [Fact]
    public async Task Setting_favorites_keeps_the_order_they_were_sent_in()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.All);

        var keys = ListWorld.Keys(ListWorld.Contact, ListWorld.Alien, ListWorld.Doubt);
        var saved = await world.Client.SetAndReadFavoritesAsync(keys);

        // Not the order they were watched in, and not alphabetical: the order the
        // owner chose. Position 0 is what the profile takes its backdrop from.
        Assert.Equal(keys, saved.Select(card => card.Key));
        Assert.Equal(keys, (await world.Client.GetFavoritesAsync()).Select(card => card.Key));
    }

    [Fact]
    public async Task A_second_write_replaces_the_whole_showcase()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.All);

        await world.Client.SetAndReadFavoritesAsync(
            ListWorld.Keys(ListWorld.Alien, ListWorld.BladeRunner));

        var replaced = await world.Client.SetAndReadFavoritesAsync(
            ListWorld.Keys(ListWorld.BladeRunner, ListWorld.Doubt));

        // Alien is gone rather than merged, and Blade Runner moved rather than
        // being added twice — the body is the complete intended list.
        Assert.Equal(ListWorld.Keys(ListWorld.BladeRunner, ListWorld.Doubt),
            replaced.Select(card => card.Key));

        var stored = await world.Factory.QueryAsync(db =>
            Task.FromResult(db.Favorites.OrderBy(f => f.Position).Select(f => f.Position).ToList()));
        Assert.Equal([0, 1], stored);
    }

    [Fact]
    public async Task An_empty_list_clears_the_showcase()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.Alien);
        await world.Client.SetAndReadFavoritesAsync(ListWorld.Keys(ListWorld.Alien));

        Assert.Empty(await world.Client.SetAndReadFavoritesAsync());
        Assert.Empty(await world.Client.GetFavoritesAsync());
    }

    [Fact]
    public async Task Any_media_type_can_be_a_favorite()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched",
            TestApi.Movie(ListWorld.Alien),
            TestApi.Series(ListWorld.Beacon),
            TestApi.Season(ListWorld.Beacon, 2));

        var saved = await world.Client.SetAndReadFavoritesAsync(
            TestApi.Series(ListWorld.Beacon),
            TestApi.Season(ListWorld.Beacon, 2),
            TestApi.Movie(ListWorld.Alien));

        Assert.Equal(["series", "season", "movie"], saved.Select(card => card.MediaType));
    }

    [Fact]
    public async Task A_favorite_survives_leaving_the_watched_list()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.Alien);
        await world.Client.SetAndReadFavoritesAsync(ListWorld.Keys(ListWorld.Alien));

        var removed = await world.Client.RemoveFromListAsync("watched", ListWorld.Alien);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Un-marking a film watched does not stop it being a favourite film. The
        // showcase is a statement about taste, not a view over a list.
        var favorites = await world.Client.GetFavoritesAsync();
        Assert.Equal(ListWorld.Keys(ListWorld.Alien), favorites.Select(card => card.Key));
        Assert.False(favorites[0].Lists.Watched);
    }

    [Fact]
    public async Task A_seventh_title_is_refused_and_changes_nothing()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.All);
        await world.AddPacedAsync("watched",
            TestApi.Series(ListWorld.Beacon), TestApi.Series(ListWorld.Wander));

        var kept = ListWorld.Keys(ListWorld.Alien);
        await world.Client.SetAndReadFavoritesAsync(kept);

        var response = await world.Client.SetFavoritesAsync(
            [.. ListWorld.AllKeys,
             TestApi.Series(ListWorld.Beacon),
             TestApi.Series(ListWorld.Wander)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.NotNull(error.Errors);
        Assert.True(error.Errors!.ContainsKey("keys"));

        // Validation happens before anything is deleted.
        Assert.Equal(kept, (await world.Client.GetFavoritesAsync()).Select(card => card.Key));
    }

    [Fact]
    public async Task A_repeated_title_is_refused()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.Alien);

        var response = await world.Client.SetFavoritesAsync(
            TestApi.Movie(ListWorld.Alien), TestApi.Movie(ListWorld.Alien));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_malformed_key_is_a_400_not_a_404()
    {
        using var world = await ListWorld.CreateAsync();

        var response = await world.Client.SetFavoritesAsync("not-a-key");

        // The same rule as every other route that takes a key: a malformed
        // identifier is a bad request, not a missing title.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    [Fact]
    public async Task A_title_the_catalog_does_not_hold_is_a_404()
    {
        using var world = await ListWorld.CreateAsync();

        // Well-formed, and known to the fake TMDB client — but never fetched, so
        // there is no local row. Favouriting does not reach upstream to make one.
        var response = await world.Client.SetFavoritesAsync(TestApi.Movie(ListWorld.Alien));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadApiErrorAsync()).Code);
        Assert.Equal(0, world.Tmdb.MovieCalls);
    }

    [Fact]
    public async Task Favorites_are_per_user()
    {
        using var world = await ListWorld.CreateAsync();
        await world.AddPacedAsync("watched", ListWorld.Alien, ListWorld.Doubt);
        await world.Client.SetAndReadFavoritesAsync(ListWorld.Keys(ListWorld.Alien));

        var (other, _) = await world.SignInAsync("stranger");

        Assert.Empty(await other.GetFavoritesAsync());
    }
}
