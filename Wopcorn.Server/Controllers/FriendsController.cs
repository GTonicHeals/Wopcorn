using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Lists;
using Wopcorn.Server.Social;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The Friends section of API-CONTRACT.md (FR-F1..FR-F6, FR-G1, NFR-4).
///
/// Every <c>friends/{userId}/…</c> read starts with
/// <see cref="FriendshipService.RequireFriendshipAsync"/> — on that request, from
/// the database, never from a list the caller was handed earlier. A non-friend
/// sees <c>403</c> and learns nothing else.
///
/// Routed from <c>api</c> rather than <c>api/friends</c> because
/// <c>GET /api/users/search</c> belongs to the same feature; a derived
/// <c>[Route]</c> replaces the base one rather than combining with it.
/// </summary>
[Route("api")]
public class FriendsController(
    WopcornDbContext db,
    FriendshipService friendships,
    TasteMatchService tasteMatch,
    ProfileService profiles,
    ListService lists) : ApiControllerBase
{
    /// <summary>FR-F1: a search is for finding one person, not for browsing.</summary>
    private const int MaxSearchResults = 20;

    public record SendRequestBody(Guid? UserId);

    // --- discovery ----------------------------------------------------------

    [HttpGet("users/search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? q, CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var me = CurrentUserId;
        var prefix = term.ToLowerInvariant();

        // ToLower().StartsWith() translates to lower(...) LIKE '…%' on every
        // relational provider — no collation trickery, no raw SQL (NFR-7).
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != me && u.DisplayName.ToLower().StartsWith(prefix))
            .OrderBy(u => u.DisplayName)
            .Take(MaxSearchResults)
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var ids = users.Select(u => u.Id).ToList();

        // One query for friendships and one for requests, for the whole result set
        // — never one per row (task 2).
        var friendIds = (await db.Friendships
                .AsNoTracking()
                .Where(f => (f.UserAId == me && ids.Contains(f.UserBId))
                            || (f.UserBId == me && ids.Contains(f.UserAId)))
                .Select(f => f.UserAId == me ? f.UserBId : f.UserAId)
                .ToListAsync(ct))
            .ToHashSet();

        var requests = await db.FriendRequests
            .AsNoTracking()
            .Where(r => (r.FromUserId == me && ids.Contains(r.ToUserId))
                        || (r.ToUserId == me && ids.Contains(r.FromUserId)))
            .Select(r => new { r.FromUserId, r.ToUserId })
            .ToListAsync(ct);

        var sent = requests.Where(r => r.FromUserId == me).Select(r => r.ToUserId).ToHashSet();
        var received = requests.Where(r => r.ToUserId == me).Select(r => r.FromUserId).ToHashSet();

        var results = users
            .Select(u => new UserSearchResult(
                u.Id.ToString(),
                u.DisplayName,
                u.ToSummary().AvatarUrl,
                friendIds.Contains(u.Id) ? UserSearchResult.Friends
                : sent.Contains(u.Id) ? UserSearchResult.RequestSent
                : received.Contains(u.Id) ? UserSearchResult.RequestReceived
                : UserSearchResult.None))
            .ToArray();

        return Ok(results);
    }

    // --- the friends screen -------------------------------------------------

    /// <summary>
    /// Friends and both request directions in one response, so the pending badge
    /// (FR-F4) is free. A constant number of queries whatever the friend count:
    /// three for the rows and at most one more for every taste match together.
    /// </summary>
    [HttpGet("friends")]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var me = CurrentUserId;

        var rows = await db.Friendships
            .AsNoTracking()
            .Include(f => f.UserA)
            .Include(f => f.UserB)
            .Where(f => f.UserAId == me || f.UserBId == me)
            .ToListAsync(ct);

        var pairs = rows
            .Select(f => (User: f.UserAId == me ? f.UserB : f.UserA, f.CreatedAt))
            .OrderBy(p => p.User.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matches = await tasteMatch.ForAsync(me, pairs.Select(p => p.User.Id).ToList(), ct);

        var friends = pairs
            .Select(p => new FriendDto(
                p.User.ToSummary(),
                p.CreatedAt,
                matches.GetValueOrDefault(p.User.Id) ?? new TasteMatch(null, 0, false)))
            .ToArray();

        // Newest first. SentAt is stored as UTC ticks (UtcInstantConverter), so this
        // is a real ORDER BY rather than an in-memory sort.
        var incoming = await RequestsAsync(
            db.FriendRequests.AsNoTracking().Include(r => r.FromUser).Where(r => r.ToUserId == me),
            r => r.FromUser, ct);

        var outgoing = await RequestsAsync(
            db.FriendRequests.AsNoTracking().Include(r => r.ToUser).Where(r => r.FromUserId == me),
            r => r.ToUser, ct);

        return Ok(new FriendsResponse(friends, incoming, outgoing));
    }

    [HttpPost("friends/requests")]
    public async Task<IActionResult> SendRequest(SendRequestBody body, CancellationToken ct = default)
    {
        if (body.UserId is not { } target || target == Guid.Empty)
        {
            return BadRequest(new ApiError("validation_failed", "Choose someone to add.",
                new Dictionary<string, string[]> { ["userId"] = ["A user id is required."] }));
        }

        // Conflicts (already_friends / request_pending), the self-request 400 and
        // the unknown-user 404 all come back as ApiException from the service.
        var request = await friendships.SendRequestAsync(CurrentUserId, target, ct);

        var recipient = await db.Users.AsNoTracking().FirstAsync(u => u.Id == target, ct);

        return StatusCode(StatusCodes.Status201Created,
            new FriendRequestDto(request.Id.ToString(), recipient.ToSummary(), request.SentAt));
    }

    [HttpPost("friends/requests/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct = default)
    {
        var me = CurrentUserId;

        // 403 for anyone but the recipient — including the sender.
        var friendship = await friendships.AcceptAsync(me, id, ct);

        var otherId = friendship.UserAId == me ? friendship.UserBId : friendship.UserAId;
        var other = await db.Users.AsNoTracking().FirstAsync(u => u.Id == otherId, ct);

        return Ok(new FriendDto(
            other.ToSummary(),
            friendship.CreatedAt,
            await tasteMatch.ForOneAsync(me, otherId, ct)));
    }

    [HttpPost("friends/requests/{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct = default)
    {
        await friendships.DeclineAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// The sender's counterpart to <see cref="Decline"/> — withdrawing a request
    /// they sent. <c>403</c> for the recipient, who has <c>decline</c> instead.
    /// </summary>
    [HttpDelete("friends/requests/{id:guid}")]
    public async Task<IActionResult> CancelRequest(Guid id, CancellationToken ct = default)
    {
        await friendships.CancelRequestAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// FR-F3. Either party may unfriend, and unfriending someone who is not a
    /// friend is still <c>204</c> — the route is idempotent, like every other
    /// removal in the API.
    /// </summary>
    [HttpDelete("friends/{userId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct = default)
    {
        await friendships.RemoveAsync(CurrentUserId, userId, ct);
        return NoContent();
    }

    // --- friend-scoped reads (NFR-4) ----------------------------------------

    [HttpGet("friends/{userId:guid}/profile")]
    public async Task<IActionResult> Profile(Guid userId, CancellationToken ct = default)
    {
        await friendships.RequireFriendshipAsync(CurrentUserId, userId, ct);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Problem(404, "not_found", "That user no longer exists.");
        }

        // The same payload the owner gets from GET /api/me/profile — one profile
        // screen, one DTO, differing only in `isSelf` and the taste match.
        return Ok(await profiles.BuildAsync(
            user,
            CurrentUserId,
            await tasteMatch.ForOneAsync(CurrentUserId, userId, ct),
            ct));
    }

    /// <summary>
    /// A friend's list, with the same query parameters as your own. The friend owns
    /// the rows; <b>you</b> own the decoration — <c>title.lists</c> and
    /// <c>title.myRating</c> describe the requesting user, and the friend's own
    /// rating rides on <c>entry.rating</c>.
    /// </summary>
    [HttpGet("friends/{userId:guid}/lists/{list}")]
    public async Task<IActionResult> FriendList(
        Guid userId,
        string list,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        [FromQuery(Name = "genre")] string[]? genre,
        [FromQuery(Name = "decade")] string[]? decade,
        [FromQuery(Name = "type")] string[]? type,
        CancellationToken ct = default)
    {
        // Before anything else, including validating the list name: a non-friend
        // must not be able to tell a real list from a typo.
        await friendships.RequireFriendshipAsync(CurrentUserId, userId, ct);

        if (!ListKinds.TryParse(list, out var kind))
        {
            return Problem(404, "not_found", "That is not a list we know about.");
        }

        var query = new ListQuery(
            sort,
            dir,
            ListsController.ParseInts(genre),
            ListsController.ParseInts(decade),
            ListsController.ParseTypes(type));

        var entries = await lists.GetAsync(userId, CurrentUserId, kind, query, ct);
        var count = await lists.CountAsync(userId, kind, ct);

        return Ok(new ListPage(count, entries));
    }

    /// <summary>
    /// One direction of the pending requests. <c>ToSummary()</c> cannot be
    /// translated to SQL, so the rows are materialised first and shaped after.
    /// </summary>
    private static async Task<IReadOnlyList<FriendRequestDto>> RequestsAsync(
        IQueryable<FriendRequest> rows,
        Func<FriendRequest, AppUser> other,
        CancellationToken ct)
    {
        var loaded = await rows.OrderByDescending(r => r.SentAt).ToListAsync(ct);

        return loaded
            .Select(r => new FriendRequestDto(r.Id.ToString(), other(r).ToSummary(), r.SentAt))
            .ToArray();
    }
}
