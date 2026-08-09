using System.Buffers.Text;
using Microsoft.AspNetCore.Identity;

namespace Wopcorn.Server.Api;

/// <summary>
/// API-CONTRACT.md "Passkeys". <paramref name="OptionsJson"/> is a JSON-encoded
/// <i>string</i>, not an object: it comes out of Identity already serialised and
/// is handed to <c>navigator.credentials</c> verbatim. Re-serialising a structure
/// WebAuthn is strict about would only invite drift.
/// </summary>
public record PasskeyOptionsResponse(string OptionsJson);

/// <summary>API-CONTRACT.md "Shared DTOs" — <c>PasskeySummary</c>.</summary>
public record PasskeySummary(string Id, string Name, DateTimeOffset CreatedAt, bool IsBackedUp);

public static class PasskeyMapping
{
    /// <summary>
    /// Credential ids are raw bytes on the wire and in the store, and base64url in
    /// JSON and in the DELETE path — base64url has no <c>+</c> or <c>/</c>, so it
    /// survives a URL segment without further escaping.
    /// </summary>
    public static string ToId(byte[] credentialId) => Base64Url.EncodeToString(credentialId);

    /// <summary>
    /// Parses a base64url id from a route. Returns <see langword="null"/> rather
    /// than throwing: a malformed id is a 404, not a 500.
    /// </summary>
    public static byte[]? FromId(string id)
    {
        try
        {
            return Base64Url.DecodeFromChars(id);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static PasskeySummary ToSummary(this UserPasskeyInfo passkey) =>
        new(ToId(passkey.CredentialId),
            string.IsNullOrWhiteSpace(passkey.Name) ? "Passkey" : passkey.Name,
            passkey.CreatedAt,
            passkey.IsBackedUp);
}
