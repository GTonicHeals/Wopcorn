using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Social;

/// <summary>
/// Every friendship rule in one place (FR-F1..FR-F6, NFR-4).
///
/// A friendship is <b>one</b> row with <c>UserAId &lt; UserBId</c>, so every read
/// and write normalises the pair through <see cref="Pair"/> first. Nothing here
/// takes an actor from anywhere but the caller's <c>CurrentUserId</c> (NFR-3), and
/// nothing caches "is a friend" — visibility is re-derived on every request, which
/// is what makes an unfriend take effect immediately.
/// </summary>
public sealed class FriendshipService(WopcornDbContext db)
{
    /// <summary>The canonical (A, B) ordering of a pair. Never write two rows.</summary>
    public static (Guid A, Guid B) Pair(Guid x, Guid y) => x.CompareTo(y) < 0 ? (x, y) : (y, x);

    public Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken ct)
    {
        if (a == b)
        {
            return Task.FromResult(false);
        }

        var (x, y) = Pair(a, b);
        return db.Friendships.AsNoTracking().AnyAsync(f => f.UserAId == x && f.UserBId == y, ct);
    }

    /// <summary>
    /// The gate in front of every friend-scoped read (NFR-4). Returns the id of the
    /// friendship row; throws <see cref="ForbiddenException"/> — never
    /// <c>404</c> — when the pair are not friends, so a stranger cannot use the
    /// status code to probe which user ids exist.
    /// </summary>
    public async Task<Guid> RequireFriendshipAsync(Guid actor, Guid other, CancellationToken ct)
    {
        if (actor == other)
        {
            throw new ForbiddenException(ForbiddenException.NotFriends);
        }

        var (x, y) = Pair(actor, other);
        var id = await db.Friendships
            .AsNoTracking()
            .Where(f => f.UserAId == x && f.UserBId == y)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);

        return id ?? throw new ForbiddenException(ForbiddenException.NotFriends);
    }

    /// <summary>Every accepted friend of <paramref name="userId"/>, as ids.</summary>
    public async Task<IReadOnlyList<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken ct) =>
        await db.Friendships
            .AsNoTracking()
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToListAsync(ct);

    /// <summary>
    /// FR-F1. A request in <b>either</b> direction blocks a new one: the reverse
    /// request is answered with <c>409 request_pending</c> rather than a second row,
    /// because the right move is to accept the one already waiting.
    /// </summary>
    public async Task<FriendRequest> SendRequestAsync(Guid from, Guid to, CancellationToken ct)
    {
        if (from == to)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "validation_failed",
                "You cannot send yourself a friend request.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == to, ct))
        {
            throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "We could not find that person.");
        }

        if (await AreFriendsAsync(from, to, ct))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "already_friends",
                "You are already friends.");
        }

        var existing = await db.FriendRequests.AnyAsync(
            r => (r.FromUserId == from && r.ToUserId == to)
                 || (r.FromUserId == to && r.ToUserId == from), ct);

        if (existing)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "request_pending",
                "There is already a friend request between you two.");
        }

        var request = new FriendRequest
        {
            Id = Guid.NewGuid(),
            FromUserId = from,
            ToUserId = to,
            SentAt = DateTimeOffset.UtcNow,
        };

        db.FriendRequests.Add(request);
        await db.SaveChangesAsync(ct);

        return request;
    }

    /// <summary>
    /// FR-F2/FR-F5. Only the <b>recipient</b> may accept — the sender gets
    /// <c>403</c>. The request row is deleted and the normalised friendship
    /// inserted in one transaction, so there is never a moment where both exist or
    /// neither does.
    /// </summary>
    public async Task<Friendship> AcceptAsync(Guid actor, Guid requestId, CancellationToken ct)
    {
        var request = await RequireRecipientAsync(actor, requestId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.FriendRequests.Remove(request);

        var (a, b) = Pair(request.FromUserId, request.ToUserId);
        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == a && f.UserBId == b, ct);

        if (friendship is null)
        {
            friendship = new Friendship
            {
                Id = Guid.NewGuid(),
                UserAId = a,
                UserBId = b,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Friendships.Add(friendship);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return friendship;
    }

    /// <summary>Only the recipient may decline; the row simply goes away.</summary>
    public async Task DeclineAsync(Guid actor, Guid requestId, CancellationToken ct)
    {
        var request = await RequireRecipientAsync(actor, requestId, ct);

        db.FriendRequests.Remove(request);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// FR-F1, the mirror of <see cref="DeclineAsync"/>: only the <b>sender</b> may
    /// withdraw a request they sent. The recipient gets <c>403</c> — they have
    /// <c>decline</c>, and letting either verb take either row would make the two
    /// sides interchangeable. Withdrawing leaves no trace, so the pair go back to
    /// <c>relationship: "none"</c> and either may send again.
    /// </summary>
    public async Task CancelRequestAsync(Guid actor, Guid requestId, CancellationToken ct)
    {
        var request = await db.FriendRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "That friend request is no longer waiting.");

        // NFR-3: the actor is the session's, and the row decides whether they may act.
        if (request.FromUserId != actor)
        {
            throw new ForbiddenException("Only the person who sent a request can withdraw it.");
        }

        db.FriendRequests.Remove(request);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// FR-F3. Either party may remove, and removing a friendship that is not there
    /// is a no-op — the route is idempotent. Deleting the row is the whole job:
    /// activity, lists and profiles are all filtered at read time, so visibility is
    /// revoked for both sides on the very next request.
    /// </summary>
    public async Task RemoveAsync(Guid actor, Guid other, CancellationToken ct)
    {
        if (actor == other)
        {
            return;
        }

        var (a, b) = Pair(actor, other);
        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == a && f.UserBId == b, ct);

        if (friendship is null)
        {
            return;
        }

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// FR-G4. The requesting user's friends who have watched this title, with their
    /// ratings — one query, ordered by rating descending with the unrated last.
    /// </summary>
    public async Task<IReadOnlyList<FriendWatchedDto>> GetFriendsWatchedAsync(
        Guid userId, TitleKey key, CancellationToken ct)
    {
        var friendIds = await GetFriendIdsAsync(userId, ct);
        if (friendIds.Count == 0)
        {
            return [];
        }

        var titleKey = key.Value;
        var rows = await db.ListEntries
            .AsNoTracking()
            .Where(e => friendIds.Contains(e.UserId)
                        && e.TitleKey == titleKey
                        && e.Kind == ListKind.Watched)
            .Select(e => new { e.User, e.Rating, e.Comment })
            .ToListAsync(ct);

        return rows
            .OrderByDescending(r => r.Rating.HasValue)
            .ThenByDescending(r => r.Rating)
            .ThenBy(r => r.User.DisplayName, StringComparer.OrdinalIgnoreCase)
            // Their note rides along with their rating (plan 10). It is already on
            // the row this query reads, so friends' comments cost nothing extra.
            .Select(r => new FriendWatchedDto(r.User.ToSummary(), r.Rating, r.Comment))
            .ToArray();
    }

    private async Task<FriendRequest> RequireRecipientAsync(
        Guid actor, Guid requestId, CancellationToken ct)
    {
        var request = await db.FriendRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "That friend request is no longer waiting.");

        // The sender is not the recipient. NFR-3: the acting user is the session's,
        // and the row decides whether they may act on it.
        if (request.ToUserId != actor)
        {
            throw new ForbiddenException("Only the person a request was sent to can answer it.");
        }

        return request;
    }
}
