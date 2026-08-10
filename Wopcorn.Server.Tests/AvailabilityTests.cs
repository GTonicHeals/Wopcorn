using System.Net;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Tests;

/// <summary>
/// Plan 09. The claims worth holding: a season answers with its series' providers,
/// a fetch inside the TTL costs nothing upstream, "nothing here" is an answer that
/// is stored rather than re-asked, an outage degrades instead of failing, and
/// <c>availableOn</c> is always the <b>viewer's</b> services and never the list
/// owner's.
/// </summary>
public class AvailabilityTests
{
    private const int Netflix = FakeTmdbClient.NetflixId;
    private const int PrimeVideo = FakeTmdbClient.PrimeVideoId;
    private const int AppleTv = FakeTmdbClient.AppleTvId;

    private const int BreakingBad = FakeTmdbClient.CollisionId;

    // --- the season fallback, which is the headline ------------------------

    [Fact]
    public async Task Season_availability_is_its_series_availability()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        // Open the season so its row (and its series') exists.
        await world.Client.GetTitleAsync(TestApi.Season(BreakingBad, 2));

        var before = world.Tmdb.WatchProviderCalls;
        var availability = await world.Client.ReadAvailabilityAsync(TestApi.Season(BreakingBad, 2));

        Assert.Equal("GB", availability.Region);
        Assert.NotNull(availability.FetchedAt);
        Assert.Equal([Netflix], availability.Kind("flatrate"));

        // The upstream call was made for the *series*: asking a season directly
        // throws inside the client, so a wrong resolution could not have got here.
        Assert.Equal(before + 1, world.Tmdb.WatchProviderCalls);
        Assert.Contains((MediaType.Series, BreakingBad), world.Tmdb.WatchProviders.Keys);
    }

    [Fact]
    public async Task A_season_card_badges_what_its_series_is_carried_on()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Series(BreakingBad));
        var entry = await world.Client.AddAndReadAsync("queue", TestApi.Season(BreakingBad, 2));

        Assert.Equal([Netflix], entry.Title.AvailableOn);
    }

    // --- the TTL and the "we looked, there is nothing" answer ---------------

    [Fact]
    public async Task A_second_request_inside_the_ttl_costs_nothing_upstream()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        var after = world.Tmdb.WatchProviderCalls;

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));

        Assert.Equal(after, world.Tmdb.WatchProviderCalls);
    }

    [Fact]
    public async Task A_title_on_no_service_records_the_fetch_and_is_not_asked_again()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        var availability = await world.Client.ReadAvailabilityAsync(
            TestApi.Movie(AvailabilityWorld.Nowhere));

        // "We looked, and there is nothing" — a timestamp with no offers beside it,
        // which is a different answer from "we never looked".
        Assert.NotNull(availability.FetchedAt);
        Assert.Empty(availability.Offers);

        var after = world.Tmdb.WatchProviderCalls;
        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Nowhere));

        Assert.Equal(after, world.Tmdb.WatchProviderCalls);
    }

    [Fact]
    public async Task Every_region_the_payload_carries_is_stored_not_just_the_one_asked_for()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));

        var regions = await world.Factory.QueryAsync(db => db.TitleAvailability
            .AsNoTracking()
            .Where(a => a.TitleKey == TestApi.Movie(AvailabilityWorld.Sicario))
            .Select(a => a.Region)
            .OrderBy(r => r)
            .ToListAsync());

        // One upstream payload answers for the whole world, so a second user in a
        // second region must not cost a second request (D-2).
        Assert.Equal(["BE", "GB"], regions);
    }

    // --- failure modes ------------------------------------------------------

    [Fact]
    public async Task An_outage_serves_stale_rows_and_never_a_503()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));

        await world.ExpireAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        world.Tmdb.Throw = true;

        var availability = await world.Client.ReadAvailabilityAsync(
            TestApi.Movie(AvailabilityWorld.Sicario));

        Assert.NotNull(availability.FetchedAt);
        Assert.Equal([Netflix], availability.Kind("flatrate"));
    }

    [Fact]
    public async Task An_outage_with_nothing_stored_is_unknown_rather_than_an_error()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        // Cache the title first, then take TMDB away.
        await world.Client.GetTitleAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        world.Tmdb.Throw = true;

        var availability = await world.Client.ReadAvailabilityAsync(
            TestApi.Movie(AvailabilityWorld.Sicario));

        Assert.Null(availability.FetchedAt);
        Assert.Empty(availability.Offers);
    }

    [Fact]
    public async Task A_viewer_with_no_region_is_told_which_field_is_missing()
    {
        using var world = await AvailabilityWorld.CreateAsync();

        var availability = await world.Client.GetAvailabilityAsync(
            TestApi.Movie(AvailabilityWorld.Sicario));
        Assert.Equal(HttpStatusCode.BadRequest, availability.StatusCode);

        var error = await availability.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.NotNull(error.Errors);
        Assert.True(error.Errors.ContainsKey("region"));

        var providers = await world.Client.GetAsync("/api/providers");
        Assert.Equal(HttpStatusCode.BadRequest, providers.StatusCode);

        // Everything else still works: availability is additive, not a gate.
        Assert.Equal(HttpStatusCode.OK, (await world.Client.GetAsync("/api/lists/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.Client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_malformed_key_is_400_and_never_404()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        var response = await world.Client.GetAvailabilityAsync("603");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await response.ReadApiErrorAsync()).Code);
    }

    // --- the directory and the services set ---------------------------------

    [Fact]
    public async Task The_directory_merges_the_film_and_tv_lists_without_a_tracking_failure()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        var providers = await world.Client.GetProvidersAsync();

        // Netflix is on both upstream lists. Without the merge against the change
        // tracker the TV pass adds a second entity with a key the film pass already
        // holds, and EF refuses it — the identical bug GenreCatalogService documents.
        Assert.Equal([Netflix, PrimeVideo, AppleTv], providers.Select(p => p.Id));
        Assert.Single(providers, p => p.Id == Netflix);
    }

    [Fact]
    public async Task The_directory_is_ordered_by_tmdb_display_priority()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        var providers = await world.Client.GetProvidersAsync();

        Assert.Equal("Netflix", providers[0].Name);
        Assert.Equal("Apple TV", providers[^1].Name);
    }

    [Fact]
    public async Task An_unknown_provider_id_is_rejected_rather_than_dropped()
    {
        using var world = await AvailabilityWorld.CreateAsync();

        var response = await world.Client.SetServicesAsync("GB", Netflix, 999_999);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadApiErrorAsync();
        Assert.Equal("validation_failed", error.Code);
        Assert.NotNull(error.Errors);
        Assert.True(error.Errors.ContainsKey("providerIds"));

        // A rejected write leaves the existing configuration exactly as it was.
        Assert.Null((await world.Client.GetMeAsync()).Region);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GBR")]
    [InlineData("g")]
    public async Task A_malformed_region_is_rejected(string region)
    {
        using var world = await AvailabilityWorld.CreateAsync();

        var response = await world.Client.SetServicesAsync(region, Netflix);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadApiErrorAsync()).Errors!.ContainsKey("region"));
    }

    [Fact]
    public async Task Setting_services_replaces_the_whole_set()
    {
        using var world = await AvailabilityWorld.CreateAsync();

        await world.Client.SetAndAssertServicesAsync("GB", Netflix, PrimeVideo);
        await world.Client.SetAndAssertServicesAsync("BE", AppleTv);

        var me = await world.Client.GetMeAsync();

        Assert.Equal("BE", me.Region);
        Assert.Equal([AppleTv], me.ProviderIds);
    }

    [Fact]
    public async Task A_lowercase_region_is_normalised()
    {
        using var world = await AvailabilityWorld.CreateAsync();

        await world.Client.SetAndAssertServicesAsync("gb", Netflix);

        Assert.Equal("GB", (await world.Client.GetMeAsync()).Region);
    }

    // --- availableOn on the card -------------------------------------------

    [Fact]
    public async Task A_viewer_with_no_services_gets_an_empty_array_and_no_offer_query()
    {
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var world = await AvailabilityWorld.CreateAsync(log);

        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        log.Clear();
        var page = await world.Client.GetListAsync("queue");

        Assert.All(page.Entries, e => Assert.Empty(e.Title.AvailableOn));
        Assert.DoesNotContain(log, line => line.Contains("TitleOffers", StringComparison.Ordinal));

        // The positive control, so the assertion above cannot pass by accident:
        // configure a service and the same request does hit the offer table.
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);
        log.Clear();
        await world.Client.GetListAsync("queue");

        Assert.Contains(log, line => line.Contains("TitleOffers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AvailableOn_is_flatrate_only()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        // Prime Video only rents Sicario in this world.
        await world.Client.SetAndAssertServicesAsync("GB", Netflix, PrimeVideo);

        var availability = await world.Client.ReadAvailabilityAsync(
            TestApi.Movie(AvailabilityWorld.Sicario));
        Assert.Equal([PrimeVideo], availability.Kind("rent"));

        var entry = await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        // "I can watch this now" and "I can pay to watch this now" are different
        // claims and one badge cannot make both (D-4).
        Assert.Equal([Netflix], entry.Title.AvailableOn);
    }

    [Fact]
    public async Task AvailableOn_lists_only_the_services_the_viewer_configured()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", AppleTv);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        var entry = await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        // Netflix carries it; this viewer does not have Netflix.
        Assert.Empty(entry.Title.AvailableOn);
    }

    [Fact]
    public async Task AvailableOn_is_region_scoped()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);
        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));

        var onNetflixInGb = await world.Client.AddAndReadAsync(
            "queue", TestApi.Movie(AvailabilityWorld.Sicario));
        Assert.Equal([Netflix], onNetflixInGb.Title.AvailableOn);

        // Same title, same services, a border away: Sicario is on Apple TV in BE.
        await world.Client.SetAndAssertServicesAsync("BE", Netflix);
        var page = await world.Client.GetListAsync("queue");

        Assert.All(page.Entries, e => Assert.Empty(e.Title.AvailableOn));
    }

    [Fact]
    public async Task A_friends_list_carries_the_viewers_availability_not_the_friends()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        var (friend, friendId) = await world.SignInAsync("friend");

        await world.Client.SetAndAssertServicesAsync("GB", Netflix);
        await friend.SetAndAssertServicesAsync("GB", AppleTv);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        await friend.AddAndReadAsync("watched", TestApi.Movie(AvailabilityWorld.Sicario));

        await TestApi.BefriendAsync(world.Client, world.UserId, friend, friendId);

        var response = await world.Client.GetAsync($"/api/friends/{friendId}/lists/watched");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsAsync<ListPageDto>();

        // The friend owns the rows; the viewer owns the badge. A badge saying "you
        // can watch this" must mean you.
        Assert.Equal([Netflix], page.Entries.Single().Title.AvailableOn);
    }

    // --- the service filter -------------------------------------------------

    [Fact]
    public async Task The_service_filter_narrows_the_queue_while_count_stays_unfiltered()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Sicario));
        await world.Client.ReadAvailabilityAsync(TestApi.Movie(AvailabilityWorld.Nowhere));

        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));
        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Nowhere));

        var page = await world.Client.GetListAsync("queue", $"?service={Netflix}");

        Assert.Equal([TestApi.Movie(AvailabilityWorld.Sicario)], page.Entries.Select(e => e.Title.Key));
        // The denominator the header divides by never moves.
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task The_service_filter_finds_a_season_through_its_series()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);

        await world.Client.ReadAvailabilityAsync(TestApi.Series(BreakingBad));
        await world.Client.AddAndReadAsync("queue", TestApi.Season(BreakingBad, 2));

        var page = await world.Client.GetListAsync("queue", $"?service={Netflix}");

        Assert.Equal([TestApi.Season(BreakingBad, 2)], page.Entries.Select(e => e.Title.Key));
    }

    [Fact]
    public async Task An_unknown_service_value_is_ignored_rather_than_rejected()
    {
        using var world = await AvailabilityWorld.CreateAsync();
        await world.Client.SetAndAssertServicesAsync("GB", Netflix);
        await world.Client.AddAndReadAsync("queue", TestApi.Movie(AvailabilityWorld.Sicario));

        // Same rule genre, decade and type follow: a stale bookmark still renders.
        var page = await world.Client.GetListAsync("queue", "?service=not-a-number");

        Assert.Single(page.Entries);
    }
}
