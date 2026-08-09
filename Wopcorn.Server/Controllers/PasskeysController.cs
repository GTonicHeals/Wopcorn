using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// Managing your own passkeys (API-CONTRACT.md, "Passkeys" — management half).
/// Every route here is the signed-in user's; there is no route that reaches
/// another account's credentials.
/// </summary>
[Route("api/me/passkeys")]
public class PasskeysController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager) : ApiControllerBase
{
    public record RegisterPasskeyRequest(
        [Required] string CredentialJson,
        [StringLength(64)] string? Name);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var user = await RequireUserAsync();
        if (user is null) return Unauthenticated();

        var passkeys = await userManager.GetPasskeysAsync(user);

        // Newest first: the one you just added is the one you are looking for.
        return Ok(passkeys
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.ToSummary())
            .ToList());
    }

    /// <summary>
    /// The challenge half of registering a passkey. Identity writes the attestation
    /// state to a short-lived cookie on this response, so the follow-up POST has to
    /// carry credentials — the two calls are one exchange.
    /// </summary>
    [HttpPost("creation-options")]
    public async Task<IActionResult> CreationOptions()
    {
        var user = await RequireUserAsync();
        if (user is null) return Unauthenticated();

        var entity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            // What the authenticator shows in its account picker. Email is the
            // stable handle; DisplayName is the friendly one.
            Name = user.Email ?? user.DisplayName,
            DisplayName = user.DisplayName,
        };

        var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(entity);
        return Ok(new PasskeyOptionsResponse(optionsJson));
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterPasskeyRequest request)
    {
        var user = await RequireUserAsync();
        if (user is null) return Unauthenticated();

        // The handler checks the credential against the options we issued above,
        // including that they were issued for this user — a credential minted for
        // someone else cannot be attached here.
        //
        // A rejected credential comes back as a failed result, but an incoherent
        // one throws: unparseable JSON, or a POST that skipped creation-options so
        // there is no challenge on file. Both are the caller's mistake, not ours.
        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(request.CredentialJson);
        }
        catch (Exception ex) when (ex is PasskeyException or JsonException or InvalidOperationException)
        {
            return Problem(400, "passkey_failed",
                "That passkey could not be registered. Try again.");
        }

        if (!attestation.Succeeded)
        {
            return Problem(400, "passkey_failed",
                "That passkey could not be registered. Try again.");
        }

        var passkey = attestation.Passkey;
        passkey.Name = string.IsNullOrWhiteSpace(request.Name)
            ? DefaultName(passkey)
            : request.Name.Trim();

        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!result.Succeeded)
        {
            return Problem(400, "passkey_failed",
                "That passkey could not be saved. Try again.");
        }

        return StatusCode(201, passkey.ToSummary());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id)
    {
        var user = await RequireUserAsync();
        if (user is null) return Unauthenticated();

        var credentialId = PasskeyMapping.FromId(id);
        if (credentialId is null)
        {
            return Problem(404, "not_found", "That passkey does not exist.");
        }

        // Scoped to this user, so a valid id belonging to someone else is a 404
        // rather than a deletion.
        var existing = await userManager.GetPasskeyAsync(user, credentialId);
        if (existing is null)
        {
            return Problem(404, "not_found", "That passkey does not exist.");
        }

        // No last-credential guard on purpose: passwords are never removed, so
        // there is always another way in (API-CONTRACT.md).
        await userManager.RemovePasskeyAsync(user, credentialId);
        return NoContent();
    }

    /// <summary>
    /// A name the user did not pick. Backed-up credentials are the synced kind
    /// (iCloud Keychain, Google Password Manager); the rest live on one device.
    /// </summary>
    private static string DefaultName(UserPasskeyInfo passkey) =>
        passkey.IsBackedUp ? "Synced passkey" : "Device passkey";

    private async Task<AppUser?> RequireUserAsync() => await userManager.GetUserAsync(User);

    private IActionResult Unauthenticated() =>
        Problem(401, "unauthenticated", "You need to sign in.");
}
