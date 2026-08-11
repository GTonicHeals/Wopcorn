using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Social;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The Suggestions section of API-CONTRACT.md (plan 10).
///
/// The verbs split by party exactly as friend requests do: <c>accept</c> and
/// <c>dismiss</c> belong to the recipient, <c>DELETE</c> to the sender, and each
/// answers <c>403</c> to the other. That split is the reason there is no single
/// "respond" route — one verb either side could reach would make the two ends of a
/// suggestion interchangeable, which they are not.
/// </summary>
[Route("api/suggestions")]
public class SuggestionsController(
    SuggestionService suggestions,
    TitleCacheService titles) : ApiControllerBase
{
    public record SendRequest(
        Guid? ToUserId,
        string? Key,
        string? Target,
        int? Position,
        string? Comment);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default) =>
        Ok(await suggestions.GetForUserAsync(CurrentUserId, ct));

    [HttpPost]
    public async Task<IActionResult> Send(SendRequest request, CancellationToken ct = default)
    {
        if (request.ToUserId is not { } to || to == Guid.Empty)
        {
            return Invalid("toUserId", "Choose someone to suggest this to.");
        }

        if (!TryParseKey(request.Key, out var parsed, out var malformed))
        {
            return malformed!;
        }

        if (!SuggestionWire.TryParseTarget(request.Target, out var target))
        {
            return Invalid("target", "A suggestion is for the watchlist or the queue.");
        }

        if (!TryNormalizeComment(request.Comment, Suggestion.MaxCommentLength, out var comment))
        {
            return Invalid(
                "comment", $"A comment is at most {Suggestion.MaxCommentLength} characters.");
        }

        // A negative position is a bad request rather than something to clamp: the
        // clamp in AddAtAsync exists for a queue that moved under the suggester, not
        // for a number that never made sense.
        if (request.Position is < 0)
        {
            return Invalid("position", "A queue position cannot be negative.");
        }

        // The catalog must already hold it, like every other write that names a title
        // (FR-B6) — and a 503 here is honest: we could not fetch it to check.
        if (await EnsureTitleAsync(titles, parsed, ct) is { } failure)
        {
            return failure;
        }

        var suggestion = await suggestions.SendAsync(
            CurrentUserId, to, parsed, target, request.Position, comment, ct);

        return StatusCode(StatusCodes.Status201Created,
            await suggestions.GetOneAsync(CurrentUserId, suggestion.Id, ct));
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct = default)
    {
        var suggestion = await suggestions.AcceptAsync(CurrentUserId, id, ct);
        return Ok(await suggestions.GetOneAsync(CurrentUserId, suggestion.Id, ct));
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct = default)
    {
        await suggestions.DismissAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// The sender withdraws. Takes back the message, never the title — see
    /// <see cref="SuggestionService.WithdrawAsync"/>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct = default)
    {
        await suggestions.WithdrawAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Trims, and treats blank as absent. A comment that is only whitespace is not a
    /// comment, and storing it would put an empty speech bubble on a card.
    /// </summary>
    internal static bool TryNormalizeComment(string? value, int maxLength, out string? comment)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            comment = null;
            return true;
        }

        comment = trimmed;
        return trimmed.Length <= maxLength;
    }

    private IActionResult Invalid(string field, string message) =>
        BadRequest(new ApiError("validation_failed", "Some fields need attention.",
            new Dictionary<string, string[]> { [field] = [message] }));
}
