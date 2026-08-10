using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Lists;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The Lists section of API-CONTRACT.md (FR-C1..FR-C7). The controller validates
/// and maps; <see cref="ListService"/> owns every mutation. The acting user is
/// <c>CurrentUserId</c> and nothing else (NFR-3).
/// </summary>
[Route("api/lists")]
public class ListsController(
    ListService lists,
    TitleCacheService titles,
    AvailabilityService availability) : ApiControllerBase
{
    /// <summary>Body of <c>PUT /api/lists/{list}/{key}</c> — every field optional.</summary>
    public record AddRequest(string[]? AlsoRemoveFrom, DateOnly? WatchedOn);

    [HttpGet("{list}")]
    public async Task<IActionResult> Get(
        string list,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        // Repeatable, and bound as strings so a junk value is ignored rather than
        // turned into a 400 by model binding.
        [FromQuery(Name = "genre")] string[]? genre,
        [FromQuery(Name = "decade")] string[]? decade,
        [FromQuery(Name = "type")] string[]? type,
        // Plan 09. Filtered against the viewer's own region, which is state on the
        // user and never a query parameter (NFR-3).
        [FromQuery(Name = "service")] string[]? service,
        CancellationToken ct = default)
    {
        if (!ListKinds.TryParse(list, out var kind))
        {
            return UnknownList();
        }

        var query = new ListQuery(
            sort,
            dir,
            ParseInts(genre),
            ParseInts(decade),
            ParseTypes(type),
            ParseInts(service),
            (await availability.ViewerAsync(CurrentUserId, ct)).Region);

        // Owner and viewer are the same person here; they differ only on
        // GET /api/friends/{userId}/lists/{list} (be-04).
        var entries = await lists.GetAsync(CurrentUserId, CurrentUserId, kind, query, ct);

        // Deliberately the *unfiltered* total, type filter included — the header
        // says "showing 12 of 84" from this one request (FR-C4).
        var count = await lists.CountAsync(CurrentUserId, kind, ct);

        return Ok(new ListPage(count, entries));
    }

    [HttpPut("{list}/{key}")]
    public async Task<IActionResult> Add(
        string list,
        string key,
        // The contract's body is optional: `PUT .../watchlist/movie-438631` with no
        // body at all is the common case.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AddRequest? request,
        CancellationToken ct = default)
    {
        if (!ListKinds.TryParse(list, out var kind))
        {
            return UnknownList();
        }

        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        if (await EnsureTitleAsync(titles, parsed, ct) is { } failure)
        {
            return failure;
        }

        var alsoRemoveFrom = ParseKinds(request?.AlsoRemoveFrom);
        // watchedOn is only meaningful on the watched list (OD-1).
        var watchedOn = kind == ListKind.Watched ? request?.WatchedOn : null;

        return Ok(await lists.AddAsync(CurrentUserId, parsed, kind, alsoRemoveFrom, watchedOn, ct));
    }

    [HttpDelete("{list}/{key}")]
    public async Task<IActionResult> Remove(string list, string key, CancellationToken ct = default)
    {
        if (!ListKinds.TryParse(list, out var kind))
        {
            return UnknownList();
        }

        if (!TryParseKey(key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        // Idempotent: removing something that is not there is still a 204.
        await lists.RemoveAsync(CurrentUserId, parsed, kind, ct);
        return NoContent();
    }

    private IActionResult UnknownList() =>
        Problem(404, "not_found", "That is not a list we know about.");

    internal static int[] ParseInts(string[]? values) =>
        (values ?? [])
        .Select(v => int.TryParse(v, out var parsed) ? parsed : (int?)null)
        .Where(v => v is not null)
        .Select(v => v!.Value)
        .Distinct()
        .ToArray();

    /// <summary>
    /// The <c>type</c> filter. Unknown values are dropped rather than rejected, the
    /// same rule genre and decade follow — an empty result means "no filter", not
    /// "match nothing".
    /// </summary>
    internal static MediaType[] ParseTypes(string[]? values) =>
        (values ?? [])
        .Select(v => MediaTypes.TryParse(v, out var parsed) ? parsed : (MediaType?)null)
        .Where(t => t is not null)
        .Select(t => t!.Value)
        .Distinct()
        .ToArray();

    /// <summary>FR-C6. Unknown list names in <c>alsoRemoveFrom</c> are ignored.</summary>
    private static ListKind[] ParseKinds(string[]? values) =>
        (values ?? [])
        .Select(v => ListKinds.TryParse(v, out var kind) ? kind : (ListKind?)null)
        .Where(k => k is not null)
        .Select(k => k!.Value)
        .Distinct()
        .ToArray();
}
