using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data;
using Wopcorn.Server.Data.Entities;
using Wopcorn.Server.Lists;

namespace Wopcorn.Server.Social;

/// <summary>
/// One friend recommending a title to another (plan 10).
///
/// The whole state machine lives here, and it has one governing rule: a
/// suggestion may write to the recipient's lists, but only ever to <b>add</b>, and
/// only ever a row it created itself. Everything below follows from that —
/// <c>added</c> exists as a state distinct from <c>pending</c> purely to record
/// which rows the suggestion owns, and <see cref="DismissAsync"/> is the only path
/// that removes anything.
///
/// Like <see cref="FriendshipService"/>, the actor is always the caller's
/// <c>CurrentUserId</c> and never comes off the wire (NFR-3), and the friendship
/// is re-derived on the request rather than trusted from a list handed over
/// earlier (NFR-4).
/// </summary>
public sealed class SuggestionService(
    WopcornDbContext db,
    FriendshipService friendships,
    ListService lists,
    TitleMapper mapper)
{
    /// <summary>
    /// FR-F-adjacent. Requires an accepted friendship, checked on this request.
    /// </summary>
    /// <param name="position">
    /// The suggester's intended queue slot. Kept only for a queue suggestion — a
    /// position on a watchlist suggestion is meaningless and is discarded rather
    /// than stored to confuse a later reader.
    /// </param>
    public async Task<Suggestion> SendAsync(
        Guid from,
        Guid to,
        TitleKey key,
        SuggestionTarget target,
        int? position,
        string? comment,
        CancellationToken ct)
    {
        if (from == to)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "validation_failed",
                "You cannot suggest a title to yourself.");
        }

        // 403 for a non-friend, before anything else is looked at.
        await friendships.RequireFriendshipAsync(from, to, ct);

        var titleKey = key.Value;
        var existing = await db.Suggestions.FirstOrDefaultAsync(
            s => s.FromUserId == from && s.ToUserId == to && s.TitleKey == titleKey, ct);

        if (existing is { IsLive: true })
        {
            throw new ApiException(StatusCodes.Status409Conflict, "suggestion_pending",
                "You have already suggested this to them.");
        }

        var recipient = await db.Users.FirstOrDefaultAsync(u => u.Id == to, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "We could not find that person.");

        var kind = target.ToListKind();
        var queuePosition = target == SuggestionTarget.Queue ? position : null;

        // Auto-add creates; it never adopts. AddAtAsync answers false when an entry
        // is already there, and a suggestion that did not create the row must not
        // claim the badge on it — "remove" would then delete the recipient's own
        // work. Such a suggestion lands pending, whatever the setting says.
        var added = recipient.AutoAddSuggestions
                    && await lists.AddAtAsync(to, key, kind, queuePosition, ct);

        // An accepted or dismissed suggestion of the same title is rewritten rather
        // than joined by a second row — the unique index says one per (from, to,
        // title), so nobody can accumulate a stack of one recommendation.
        var suggestion = existing ?? new Suggestion
        {
            Id = Guid.NewGuid(),
            FromUserId = from,
            ToUserId = to,
            TitleKey = titleKey,
        };

        suggestion.Target = target;
        suggestion.Position = queuePosition;
        suggestion.Comment = comment;
        suggestion.State = added ? SuggestionState.Added : SuggestionState.Pending;
        suggestion.SentAt = DateTimeOffset.UtcNow;
        suggestion.RespondedAt = null;

        if (existing is null)
        {
            db.Suggestions.Add(suggestion);
        }

        await db.SaveChangesAsync(ct);

        return suggestion;
    }

    /// <summary>
    /// The recipient's verb. Adds the entry if it is not there yet, then stops the
    /// badge: <c>TitleCard.suggestion</c> goes null and the accept/remove line with
    /// it, while the title stays exactly where it is.
    ///
    /// Accepting a title the recipient already has is an idempotent add that leaves
    /// the existing entry untouched, <c>AddedAt</c> included — accepting is an
    /// acknowledgement, not a re-add.
    /// </summary>
    public async Task<Suggestion> AcceptAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var suggestion = await RequireRecipientAsync(actor, id, ct);

        if (suggestion.State == SuggestionState.Pending)
        {
            await lists.AddAtAsync(
                actor,
                TitleKey.Parse(suggestion.TitleKey),
                suggestion.Target.ToListKind(),
                suggestion.Position,
                ct);
        }

        suggestion.State = SuggestionState.Accepted;
        suggestion.RespondedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return suggestion;
    }

    /// <summary>
    /// The other recipient's verb, and the only path here that removes anything.
    ///
    /// The entry goes only when the suggestion created it — state <c>added</c>. A
    /// pending suggestion never wrote to a list, and an accepted one is the
    /// recipient's own by then, so neither may take a title away.
    ///
    /// Like declining a friend request it leaves no trace: the row is deleted, so it
    /// vanishes from the sender's <c>outgoing</c> too and they may suggest again.
    /// </summary>
    public async Task DismissAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var suggestion = await RequireRecipientAsync(actor, id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (suggestion.State == SuggestionState.Added)
        {
            await lists.RemoveWithinTransactionAsync(
                actor, TitleKey.Parse(suggestion.TitleKey), suggestion.Target.ToListKind(), ct);
        }

        db.Suggestions.Remove(suggestion);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The sender's counterpart, mirroring <c>DELETE /api/friends/requests/{id}</c>:
    /// <c>403</c> for the recipient, who has accept and dismiss instead.
    ///
    /// Withdrawing takes back the <b>message</b> and never the title. Even on an
    /// <c>added</c> suggestion the list entry stays — by then it is a row in someone
    /// else's queue, possibly moved, and a friend does not get to reach in and
    /// delete it. All that disappears is the attribution.
    /// </summary>
    public async Task WithdrawAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var suggestion = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "That suggestion is no longer there.");

        if (suggestion.FromUserId != actor)
        {
            throw new ForbiddenException("Only the person who made a suggestion can withdraw it.");
        }

        db.Suggestions.Remove(suggestion);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Both directions of the suggestions screen, decorated for the viewer.
    ///
    /// <c>incoming</c> is what is waiting on them — <c>pending</c> and
    /// <c>added</c>. <c>outgoing</c> also carries <c>accepted</c>, so the sender can
    /// see what became of what they sent, and never dismissed ones, which leave no
    /// trace by design.
    /// </summary>
    public async Task<SuggestionsResponse> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await WithDetail()
            .Where(s => (s.ToUserId == userId && s.State != SuggestionState.Accepted)
                        || s.FromUserId == userId)
            .OrderByDescending(s => s.SentAt)
            .ToListAsync(ct);

        // One decoration pass for both directions together, so the screen costs a
        // constant number of queries however many suggestions are on it.
        var dtos = await ToDtosAsync(userId, rows, ct);

        return new SuggestionsResponse(
            rows.Where(s => s.ToUserId == userId)
                .Select(s => dtos[s.Id])
                .ToArray(),
            rows.Where(s => s.FromUserId == userId)
                .Select(s => dtos[s.Id])
                .ToArray());
    }

    /// <summary>
    /// One row, decorated the same way the list decorates its own — so a write's
    /// response and the next <c>GET</c> cannot disagree about the shape of what was
    /// written.
    ///
    /// This exists rather than filtering <see cref="GetForUserAsync"/> because that
    /// method deliberately hides accepted suggestions from <c>incoming</c>, and the
    /// response to <see cref="AcceptAsync"/> is precisely an accepted suggestion the
    /// recipient needs back.
    /// </summary>
    public async Task<SuggestionDto> GetOneAsync(Guid viewerId, Guid id, CancellationToken ct)
    {
        var row = await WithDetail().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "That suggestion is no longer there.");

        return (await ToDtosAsync(viewerId, [row], ct))[row.Id];
    }

    private IQueryable<Suggestion> WithDetail() =>
        db.Suggestions
            .AsNoTracking()
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .Include(s => s.Title)
            .ThenInclude(t => t.Genres);

    /// <summary>
    /// Turns rows into wire shapes in a constant number of queries: one for the
    /// viewer's card decoration and one for the senders' ratings, whatever the count.
    /// </summary>
    private async Task<Dictionary<Guid, SuggestionDto>> ToDtosAsync(
        Guid viewerId, IReadOnlyCollection<Suggestion> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var context = await mapper.LoadUserContextAsync(
            viewerId, rows.Select(s => s.TitleKey).Distinct(StringComparer.Ordinal).ToList(), ct);

        var ratings = await RatingsOfSendersAsync(rows, ct);

        return rows.ToDictionary(
            s => s.Id,
            s => new SuggestionDto(
                s.Id.ToString(),
                s.FromUser.ToSummary(),
                s.ToUser.ToSummary(),
                mapper.ToCard(s.Title, context),
                s.Target.ToWire(),
                s.Position,
                s.Comment,
                ratings.GetValueOrDefault((s.FromUserId, s.TitleKey)),
                s.State.ToWire(),
                s.SentAt));
    }

    /// <summary>
    /// Every suggestion of one title <b>to</b> one viewer, for the title screen —
    /// including accepted ones, which is the difference between this and the badge.
    /// Ordered newest first, so it reads the same way the inbox does.
    /// </summary>
    public async Task<IReadOnlyList<SuggestionNote>> GetNotesAsync(
        Guid viewerId, TitleKey key, CancellationToken ct)
    {
        var titleKey = key.Value;

        var rows = await db.Suggestions
            .AsNoTracking()
            .Include(s => s.FromUser)
            .Where(s => s.ToUserId == viewerId && s.TitleKey == titleKey)
            .OrderByDescending(s => s.SentAt)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var ratings = await RatingsOfSendersAsync(rows, ct);

        return rows
            .Select(s => new SuggestionNote(
                s.Id.ToString(),
                s.FromUser.ToSummary(),
                s.Comment,
                ratings.GetValueOrDefault((s.FromUserId, s.TitleKey)),
                s.Target.ToWire(),
                s.Position,
                s.State.ToWire(),
                s.SentAt))
            .ToArray();
    }

    /// <summary>
    /// What each suggester rated the thing they suggested — "my friend gave this a 9
    /// and thinks I should watch it" is most of the reason to look at a suggestion at
    /// all. One query for the whole set, never one per row.
    /// </summary>
    private async Task<Dictionary<(Guid UserId, string TitleKey), int?>> RatingsOfSendersAsync(
        IReadOnlyCollection<Suggestion> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var senderIds = rows.Select(s => s.FromUserId).Distinct().ToList();
        var keys = rows.Select(s => s.TitleKey).Distinct(StringComparer.Ordinal).ToList();

        // The cross product is filtered back down in memory: a query per (sender,
        // title) pair would be one per row, and OR-ing the pairs together produces
        // SQL that grows with the page rather than with its parameters.
        var pairs = rows.Select(s => (s.FromUserId, s.TitleKey)).ToHashSet();

        var entries = await db.ListEntries
            .AsNoTracking()
            .Where(e => e.Kind == ListKind.Watched
                        && senderIds.Contains(e.UserId)
                        && keys.Contains(e.TitleKey))
            .Select(e => new { e.UserId, e.TitleKey, e.Rating })
            .ToListAsync(ct);

        return entries
            .Where(e => pairs.Contains((e.UserId, e.TitleKey)))
            .ToDictionary(e => (e.UserId, e.TitleKey), e => e.Rating);
    }

    /// <summary>
    /// The gate on both recipient verbs. A sender acting on their own suggestion
    /// gets <c>403</c>, never a silent success — the two sides of a suggestion are
    /// not interchangeable.
    /// </summary>
    private async Task<Suggestion> RequireRecipientAsync(
        Guid actor, Guid id, CancellationToken ct)
    {
        var suggestion = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "not_found",
                "That suggestion is no longer waiting.");

        if (suggestion.ToUserId != actor)
        {
            throw new ForbiddenException(
                "Only the person a suggestion was made to can answer it.");
        }

        return suggestion;
    }
}
