using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Social;

/// <summary>
/// Expires every cached taste match belonging to one actor at once.
///
/// Singleton, because the token has to outlive the request that created the cache
/// entry. Each actor owns a <see cref="CancellationTokenSource"/>; cancelling it
/// evicts every entry that registered the matching change token — which is how
/// "invalidate the actor's whole cache entry set on any rating write" is done
/// without enumerating keys, something <see cref="IMemoryCache"/> cannot do.
/// </summary>
public sealed class TasteMatchInvalidator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public IChangeToken TokenFor(Guid actor) =>
        new CancellationChangeToken(_sources.GetOrAdd(actor, _ => new CancellationTokenSource()).Token);

    /// <summary>Called whenever <paramref name="actor"/>'s ratings change.</summary>
    public void Invalidate(Guid actor)
    {
        if (_sources.TryRemove(actor, out var source))
        {
            source.Cancel();
            source.Dispose();
        }
    }
}

/// <summary>
/// FR-G5/FR-G6 — how alike two people's ratings are.
///
/// <code>
/// shared = titles both have rated (Watched entries with Rating != null)
/// n      = |shared|
/// MAD    = (1/n) * Σ |ratingA(f) - ratingB(f)|     // half-star units, 1..10
/// score  = round(100 * (1 - MAD / 9))               // 9 = the largest possible gap
/// </code>
///
/// The score is always computed and always returned; <c>qualified</c> is what says
/// whether it means anything yet. Below <see cref="MinimumOverlap"/> shared titles
/// the client must not present it as a headline figure (FR-G6).
/// </summary>
public sealed class TasteMatchService(
    WopcornDbContext db,
    IMemoryCache cache,
    TasteMatchInvalidator invalidator)
{
    /// <summary>Fewer shared ratings than this and the score is not yet meaningful.</summary>
    public const int MinimumOverlap = 5;

    /// <summary>The widest gap between two 1–10 ratings, and so the divisor.</summary>
    public const int MaxDifference = 9;

    /// <summary>
    /// Deliberately one-sided: the friend's cached view of the pair may lag by up
    /// to this long after the actor rates something. Accepted (be-04 task 4) rather
    /// than building cross-user invalidation.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The whole formula, as pure arithmetic — no database, no cache, so the
    /// boundary cases are unit-testable. <paramref name="sharedCount"/> of zero is
    /// <c>score: null</c>, not a division by zero.
    /// </summary>
    public static TasteMatch Compute(int sharedCount, long absoluteDifferenceSum)
    {
        if (sharedCount <= 0)
        {
            return new TasteMatch(null, 0, false);
        }

        var mad = (double)absoluteDifferenceSum / sharedCount;
        var score = (int)Math.Round(100 * (1 - mad / MaxDifference), MidpointRounding.AwayFromZero);

        return new TasteMatch(score, sharedCount, sharedCount >= MinimumOverlap);
    }

    /// <summary>
    /// Taste match against a whole set of friends. Whatever the cache does not
    /// already hold is resolved in <b>one</b> grouped join, so <c>GET /api/friends</c>
    /// with twenty friends is still one query (be-04 "Done when").
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, TasteMatch>> ForAsync(
        Guid actor, IReadOnlyCollection<Guid> friendIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, TasteMatch>();
        var missing = new List<Guid>();

        foreach (var friendId in friendIds.Distinct())
        {
            if (cache.TryGetValue(Key(actor, friendId), out TasteMatch? cached) && cached is not null)
            {
                result[friendId] = cached;
            }
            else
            {
                missing.Add(friendId);
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        var mine = db.ListEntries
            .AsNoTracking()
            .Where(e => e.UserId == actor && e.Kind == ListKind.Watched && e.Rating != null);

        var theirs = db.ListEntries
            .AsNoTracking()
            .Where(e => missing.Contains(e.UserId) && e.Kind == ListKind.Watched && e.Rating != null);

        // |a - b| as a CASE rather than Math.Abs: plain comparison and subtraction
        // translate on every relational provider (NFR-7).
        // Matched on the title key, so a film and a series that happen to share a
        // TMDB id are never mistaken for the same thing.
        var rows = await mine
            .Join(theirs,
                m => m.TitleKey,
                t => t.TitleKey,
                (m, t) => new { t.UserId, Mine = m.Rating!.Value, Theirs = t.Rating!.Value })
            .GroupBy(x => x.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Shared = g.Count(),
                Difference = g.Sum(x => x.Mine > x.Theirs ? x.Mine - x.Theirs : x.Theirs - x.Mine),
            })
            .ToListAsync(ct);

        var byFriend = rows.ToDictionary(r => r.UserId);

        foreach (var friendId in missing)
        {
            var match = byFriend.TryGetValue(friendId, out var row)
                ? Compute(row.Shared, row.Difference)
                : Compute(0, 0);       // no overlap at all

            Store(actor, friendId, match);
            result[friendId] = match;
        }

        return result;
    }

    /// <summary>Convenience for the single-friend surfaces (accept, profile).</summary>
    public async Task<TasteMatch> ForOneAsync(Guid actor, Guid friendId, CancellationToken ct) =>
        (await ForAsync(actor, [friendId], ct)).TryGetValue(friendId, out var match)
            ? match
            : new TasteMatch(null, 0, false);

    private void Store(Guid actor, Guid friendId, TasteMatch match) =>
        cache.Set(Key(actor, friendId), match, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
        }.AddExpirationToken(invalidator.TokenFor(actor)));

    private static string Key(Guid actor, Guid friendId) => $"taste:{actor}:{friendId}";
}
