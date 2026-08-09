using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Lists;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// Queue ordering (FR-D2..FR-D5). Both routes <b>rewrite stored positions</b> and
/// echo the authoritative order so an optimistic client can reconcile.
/// Reordering emits no activity: a position is private, not feed-worthy.
///
/// The queue mixes media types in one order — a film, a series and a season are
/// three keys in the same list.
/// </summary>
[Route("api/queue")]
public class QueueController(ListService lists) : ApiControllerBase
{
    public record OrderRequest(string[]? Keys);

    public record SortRequest(string? Preset, string? Dir);

    [HttpPut("order")]
    public async Task<IActionResult> Order(OrderRequest request, CancellationToken ct = default)
    {
        var (ok, keys) = await lists.ReorderQueueAsync(CurrentUserId, request.Keys ?? [], ct);

        if (!ok)
        {
            // FR-D5: the client's picture of the queue is stale. Hand back the real
            // order rather than applying a partial reorder.
            return Conflict(new QueueOutOfSync(
                "queue_out_of_sync",
                "Your queue changed somewhere else. This is its current order.",
                keys));
        }

        return Ok(new QueueOrder(keys));
    }

    [HttpPost("sort")]
    public async Task<IActionResult> Sort(SortRequest request, CancellationToken ct = default)
    {
        // An unknown preset falls back to `added` — never a 400 (FR-C5's rule,
        // applied consistently).
        var keys = await lists.SortQueueAsync(CurrentUserId, request.Preset, request.Dir, ct);
        return Ok(new QueueOrder(keys));
    }
}
