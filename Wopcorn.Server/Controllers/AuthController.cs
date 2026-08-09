using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wopcorn.Server.Api;
using Wopcorn.Server.Auth;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Controllers;

[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IPasswordResetMailer mailer,
    IOptions<SmtpOptions> smtpOptions) : ApiControllerBase
{
    // Validation attributes go on the record's constructor parameters: MVC throws
    // if it finds them on the generated properties instead.
    public record RegisterRequest(
        [Required, EmailAddress] string Email,
        [Required, MinLength(8)] string Password,
        [Required, StringLength(32, MinimumLength = 2)] string DisplayName);

    public record LoginRequest(
        [Required] string Email,
        [Required] string Password);

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var displayName = request.DisplayName.Trim();
        if (displayName.Length is < 2 or > 32)
        {
            return BadRequest(new ApiError("validation_failed", "Some fields need attention.",
                new Dictionary<string, string[]>
                {
                    ["displayName"] = ["Display name must be between 2 and 32 characters."],
                }));
        }

        // FR-A2: reject the taken name before creating anything.
        if (await userManager.Users.AnyAsync(u => u.DisplayName == displayName))
        {
            return Problem(409, "display_name_taken", "That display name is already taken.");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new ApiError("validation_failed", "Some fields need attention.",
                result.Errors
                    .GroupBy(e => IdentityErrorField(e.Code))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        }

        await signInManager.SignInAsync(user, isPersistent: true);   // FR-A4
        return Ok(user.ToSummary());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: true, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                return Ok(user.ToSummary());
            }
        }

        // Identical answer for an unknown email and a wrong password — no user
        // enumeration.
        return Problem(401, "unauthenticated", "Email or password is incorrect.");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // Anonymous so the client's boot check is not a console error.
        if (User.Identity?.IsAuthenticated != true)
        {
            return Problem(401, "unauthenticated", "You need to sign in.");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            // Cookie survived the user row; treat it as signed out.
            await signInManager.SignOutAsync();
            return Problem(401, "unauthenticated", "You need to sign in.");
        }

        return Ok(user.ToSummary());
    }

    // ----------------------------------------------------------- password reset

    public record ForgotPasswordRequest([Required] string Email);

    public record ResetPasswordRequest(
        [Required] string Email,
        [Required] string Token,
        [Required, MinLength(8)] string Password);

    /// <summary>
    /// Always <c>202</c>, whatever the email is (API-CONTRACT.md). The work happens
    /// behind an answer that is identical for a real address, an unknown one, and
    /// a malformed one — the same no-enumeration rule as <c>login</c>.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var link = ResetLink(user.Email!, token);
            await mailer.SendAsync(user.Email!, user.DisplayName, link, HttpContext.RequestAborted);
        }

        return Accepted();
    }

    /// <summary>
    /// Spends a single-use reset token. Deliberately does <b>not</b> sign the user
    /// in — a reset can be driven from a link, and a link should not mint a
    /// session. They land back on /login and type the new password once.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same answer as a bad token: an unknown address must not be
            // distinguishable here either.
            return Problem(400, "invalid_reset_token", ResetTokenMessage);
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.Password);
        if (result.Succeeded)
        {
            return NoContent();
        }

        // Identity folds two very different failures into one result. A bad token
        // is not the user's typing and has no field to attach to; a rejected
        // password does, and belongs on the field so the form can show it.
        if (result.Errors.Any(e => e.Code is "InvalidToken" or "InvalidPasswordResetToken"))
        {
            return Problem(400, "invalid_reset_token", ResetTokenMessage);
        }

        return BadRequest(new ApiError("validation_failed", "Some fields need attention.",
            result.Errors
                .GroupBy(e => IdentityErrorField(e.Code))
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
    }

    private const string ResetTokenMessage =
        "That reset link is no longer valid. Ask for a new one.";

    /// <summary>
    /// The link the mail carries. <see cref="SmtpOptions.AppBaseUrl"/> wins when
    /// set; otherwise the origin the request arrived on, which is correct for the
    /// same-origin production deployment.
    /// </summary>
    private string ResetLink(string email, string token)
    {
        var configured = smtpOptions.Value.AppBaseUrl;
        var origin = string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}"
            : configured.TrimEnd('/');

        return $"{origin}/reset-password" +
               $"?email={Uri.EscapeDataString(email)}" +
               $"&token={Uri.EscapeDataString(token)}";
    }

    // ---------------------------------------------------------- passkey sign-in

    public record PasskeyRequestOptionsRequest(string? Email);

    public record PasskeyCredentialRequest([Required] string CredentialJson);

    /// <summary>
    /// The challenge half of a passkey sign-in. Identity stashes the assertion
    /// state in a short-lived cookie on this response, which is why the follow-up
    /// <c>signin</c> call has to travel with credentials.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("passkeys/request-options")]
    public async Task<IActionResult> PasskeyRequestOptions(PasskeyRequestOptionsRequest request)
    {
        // No email means a usernameless (discoverable) sign-in — the browser
        // offers whichever account the credential belongs to. With an email we
        // narrow the allow-list, and an unknown one still gets options rather
        // than a 404: no enumeration here either.
        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await userManager.FindByEmailAsync(request.Email);

        var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
        return Ok(new PasskeyOptionsResponse(optionsJson));
    }

    [AllowAnonymous]
    [HttpPost("passkeys/signin")]
    public async Task<IActionResult> PasskeySignIn(PasskeyCredentialRequest request)
    {
        // Assertion rather than PasskeySignInAsync: this hands back the user, which
        // the response needs, and keeps the sign-in explicit and persistent (FR-A4).
        //
        // Identity reports a *rejected* credential as a failed result but throws
        // when the exchange never made sense — unparseable JSON, or a POST that
        // skipped request-options so there is no challenge to check against. This
        // is an anonymous endpoint, so both have to be an answer rather than a 500.
        PasskeyAssertionResult<AppUser> assertion;
        try
        {
            assertion = await signInManager.PerformPasskeyAssertionAsync(request.CredentialJson);
        }
        catch (Exception ex) when (ex is PasskeyException or JsonException or InvalidOperationException)
        {
            return Problem(401, "unauthenticated", "That passkey was not accepted.");
        }

        if (!assertion.Succeeded)
        {
            return Problem(401, "unauthenticated", "That passkey was not accepted.");
        }

        var user = assertion.User;

        // Persists the bumped signature counter — the replay defence only works if
        // the new count is stored.
        await userManager.AddOrUpdatePasskeyAsync(user, assertion.Passkey);
        await signInManager.SignInAsync(user, isPersistent: true);

        return Ok(user.ToSummary());
    }

    private static string IdentityErrorField(string code) => code switch
    {
        "DuplicateEmail" or "InvalidEmail" or "DuplicateUserName" or "InvalidUserName" => "email",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        _ => "",
    };
}
