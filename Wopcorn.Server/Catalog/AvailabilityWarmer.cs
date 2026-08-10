using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Catalog;

/// <summary>
/// Keeps availability warm for the titles people have actually queued.
///
/// The problem it exists for, stated plainly: availability is fetched when a title
/// is <b>opened</b>, and nobody opens the titles already in their queue. Without
/// warming, <c>availableOn</c> is empty for most rows and the Streaming filter is
/// a feature that shows nothing.
///
/// This is the app's first background service, which is the real architectural
/// cost of plan 09 and the reason it is kept as small and as timid as it is: it
/// takes a bounded batch, spends at most one upstream request a second, and wraps
/// every pass in a catch — the app is fully usable with no availability data at
/// all, so a warmer must never be the reason the host goes down.
/// </summary>
public sealed class AvailabilityWarmer(
    IServiceScopeFactory scopes,
    ILogger<AvailabilityWarmer> logger) : BackgroundService
{
    /// <summary>How long after startup the first pass runs. Boot comes first.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// One request per second, hard. FR-B8 budgets ~50/s, so this spends 2% of it
    /// and will never be the reason someone's search is throttled.
    /// </summary>
    public static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Titles refreshed per pass. Tens of users with queues of tens of titles is a
    /// working set in the low hundreds, so a full sweep is a few passes and steady
    /// state — behind the 24-hour TTL — is a few hundred requests a day.
    /// </summary>
    public const int BatchSize = 40;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Deliberately broad. Anything that reaches here would otherwise take
                // the host with it, and availability is the one feature the app is
                // entirely usable without.
                logger.LogError(ex, "An availability warming pass failed.");
            }

            try
            {
                await Task.Delay(PassInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One pass: find the stalest <c>(title, region)</c> pairs anyone is waiting on
    /// and refresh them, one request per second.
    /// </summary>
    /// <remarks>
    /// <see cref="BackgroundService"/> is a singleton and <c>WopcornDbContext</c> is
    /// scoped, so a scope is opened per pass. Injecting the context directly
    /// compiles and then throws at resolution.
    /// </remarks>
    private async Task RunPassAsync(CancellationToken ct)
    {
        List<(TitleKey Key, string Region)> batch;

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WopcornDbContext>();
            batch = await NextBatchAsync(db, ct);
        }

        if (batch.Count == 0)
        {
            return;
        }

        logger.LogInformation("Warming availability for {Count} titles.", batch.Count);

        foreach (var (key, region) in batch)
        {
            ct.ThrowIfCancellationRequested();

            // A scope per title rather than per pass: the pass spans minutes at one
            // request a second, and a DbContext held open that long is a change
            // tracker that only grows.
            using (var scope = scopes.CreateScope())
            {
                var availability = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
                await availability.RefreshAsync(key, region, ct);
            }

            await Task.Delay(RequestInterval, ct);
        }
    }

    /// <summary>
    /// The working set: titles on <b>any</b> user's Queue or Watchlist, paired with
    /// the region that user is in, stalest first with never-fetched pairs ahead of
    /// everything.
    /// </summary>
    /// <remarks>
    /// Watched is excluded deliberately — it is the largest list and the one nobody
    /// needs availability for. Seasons resolve to their series, so a queue of five
    /// seasons of one show is one request rather than five.
    ///
    /// Public so the warmer's suite can assert on the working set without sitting
    /// through the fifteen-minute interval and the one-second-per-title pacing that
    /// surround it — the selection is the part with rules in it.
    /// </remarks>
    public static async Task<List<(TitleKey Key, string Region)>> NextBatchAsync(
        WopcornDbContext db, CancellationToken ct)
    {
        var wanted = await db.ListEntries
            .AsNoTracking()
            .Where(e => (e.Kind == ListKind.Queue || e.Kind == ListKind.Watchlist)
                        && e.User.Region != null)
            .Select(e => new
            {
                // A season's providers are its series', so warm the parent.
                TitleKey = e.Title.ParentKey ?? e.TitleKey,
                Region = e.User.Region!,
            })
            .Distinct()
            .ToListAsync(ct);

        if (wanted.Count == 0)
        {
            return [];
        }

        var keys = wanted.Select(w => w.TitleKey).Distinct().ToList();

        var stamps = await db.TitleAvailability
            .AsNoTracking()
            .Where(a => keys.Contains(a.TitleKey))
            .Select(a => new { a.TitleKey, a.Region, a.FetchedAt })
            .ToDictionaryAsync(a => (a.TitleKey, a.Region), a => a.FetchedAt, ct);

        var now = DateTimeOffset.UtcNow;

        return wanted
            .Select(w => new
            {
                w.TitleKey,
                w.Region,
                FetchedAt = stamps.TryGetValue((w.TitleKey, w.Region), out var at)
                    ? at
                    : (DateTimeOffset?)null,
            })
            // Anything still inside the TTL is not worth a request.
            .Where(w => w.FetchedAt is not { } at || !AvailabilityService.IsFresh(at, now))
            .OrderBy(w => w.FetchedAt ?? DateTimeOffset.MinValue)
            .Take(BatchSize)
            .Select(w => (TitleKey.Parse(w.TitleKey), w.Region))
            .ToList();
    }
}
