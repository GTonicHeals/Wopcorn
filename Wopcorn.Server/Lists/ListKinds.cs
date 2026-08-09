using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Lists;

/// <summary>
/// The <c>{list}</c> path segment. Anything else is a 404 at the controller,
/// never a 400 — same shape as <c>DiscoverFeeds.TryParse</c>.
/// </summary>
public static class ListKinds
{
    public static bool TryParse(string? value, out ListKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "watched":
                kind = ListKind.Watched;
                return true;
            case "watchlist":
                kind = ListKind.Watchlist;
                return true;
            case "queue":
                kind = ListKind.Queue;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
