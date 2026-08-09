using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Wopcorn.Server.Social;

/// <summary>
/// The feed's keyset position (FR-G3): the instant and id of the last item the
/// client has seen. Never an offset — paging must not get slower, or start
/// duplicating and skipping rows, as history grows.
///
/// The wire form is base64url over <c>{OccurredAt:O}|{Id}</c>. It is opaque to the
/// client by contract, but it round-trips exactly, so a page boundary is
/// reproducible in a test.
/// </summary>
public readonly record struct FeedCursor(DateTimeOffset OccurredAt, Guid Id)
{
    private const char Separator = '|';

    public string Encode() =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes($"{OccurredAt.ToString("O", CultureInfo.InvariantCulture)}{Separator}{Id:D}"));

    /// <summary>
    /// Anything unparseable is <c>false</c>, so the controller can answer
    /// <c>400 validation_failed</c> — a hand-edited query string must never be a
    /// 500.
    /// </summary>
    public static bool TryParse(string? value, out FeedCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value));
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var separator = decoded.LastIndexOf(Separator);
        if (separator <= 0 || separator == decoded.Length - 1)
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                decoded[..separator], "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var occurredAt))
        {
            return false;
        }

        if (!Guid.TryParseExact(decoded[(separator + 1)..], "D", out var id))
        {
            return false;
        }

        cursor = new FeedCursor(occurredAt, id);
        return true;
    }
}
