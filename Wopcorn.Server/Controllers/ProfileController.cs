using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Lists;
using Wopcorn.Server.Social;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// Your own profile page, and the favourites showcase on it (API-CONTRACT.md,
/// "Profile and favourites").
///
/// Separate from <see cref="MeController"/>, which owns the account itself — the
/// display name, the avatar file. This is the page other people see; that is the
/// settings behind it.
///
/// The favourites routes are the only writes here, and they can only ever write
/// the caller's own showcase: <c>CurrentUserId</c> is the sole source of the owner
/// id (NFR-3). There is no route by which one user edits another's.
/// </summary>
[Route("api/me")]
public class ProfileController(
    WopcornDbContext db,
    ProfileService profiles,
    FavoritesService favorites) : ApiControllerBase
{
    public record FavoritesRequest(IReadOnlyList<string>? Keys);

    /// <summary>
    /// The same payload a friend gets from
    /// <c>GET /api/friends/{userId}/profile</c>, with <c>tasteMatch: null</c> —
    /// there is nobody to compare you against.
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        var me = CurrentUserId;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me, ct);
        if (user is null)
        {
            return Problem(404, "not_found", "That user no longer exists.");
        }

        return Ok(await profiles.BuildAsync(user, me, tasteMatch: null, ct));
    }

    [HttpGet("favorites")]
    public async Task<IActionResult> Favorites(CancellationToken ct = default)
    {
        var me = CurrentUserId;
        return Ok(await favorites.GetAsync(me, me, ct));
    }

    /// <summary>
    /// Replaces the whole showcase, like <c>PUT /api/queue/order</c> replaces the
    /// whole queue: the body is the complete intended list, so add, remove and
    /// reorder are one request and an empty array clears it.
    /// </summary>
    [HttpPut("favorites")]
    public async Task<IActionResult> SetFavorites(
        FavoritesRequest? request, CancellationToken ct = default)
    {
        // The rejections (a malformed key, a repeat, more than six, a title the
        // catalog does not hold) all arrive as ApiException from the service, which
        // validates before it deletes — a refused write leaves the showcase intact.
        return Ok(await favorites.ReplaceAsync(CurrentUserId, request?.Keys, ct));
    }
}
