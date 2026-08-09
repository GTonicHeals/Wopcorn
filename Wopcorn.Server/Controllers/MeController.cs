using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Api;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Controllers;

[Route("api/me")]
public class MeController(
    UserManager<AppUser> userManager,
    IWebHostEnvironment environment) : ApiControllerBase
{
    /// <summary>2 MB (FR-A7).</summary>
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedAvatarTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    // On the constructor parameter, not the property — see AuthController.
    public record RenameRequest([Required, StringLength(32, MinimumLength = 2)] string DisplayName);

    public record AvatarResponse(string? AvatarUrl);

    [HttpPut]
    public async Task<IActionResult> Rename(RenameRequest request)
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

        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null)
        {
            return Problem(404, "not_found", "That user no longer exists.");
        }

        // A no-op rename to the user's own current name succeeds.
        if (!string.Equals(user.DisplayName, displayName, StringComparison.Ordinal))
        {
            var id = user.Id;
            if (await userManager.Users.AnyAsync(u => u.DisplayName == displayName && u.Id != id))
            {
                return Problem(409, "display_name_taken", "That display name is already taken.");
            }

            user.DisplayName = displayName;
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                // The unique index is the last line of defence against a race.
                return Problem(409, "display_name_taken", "That display name is already taken.");
            }
        }

        return Ok(user.ToSummary());
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiError("validation_failed", "Choose an image to upload."));
        }

        if (file.Length > MaxAvatarBytes)
        {
            return BadRequest(new ApiError("validation_failed", "That image is larger than 2 MB."));
        }

        if (file.ContentType is null ||
            !AllowedAvatarTypes.TryGetValue(file.ContentType, out var extension))
        {
            return BadRequest(new ApiError("validation_failed", "Avatars must be a PNG, JPEG, or WebP image."));
        }

        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null)
        {
            return Problem(404, "not_found", "That user no longer exists.");
        }

        // Never the client-supplied filename: the name is derived from the user id.
        var relativePath = $"avatars/{user.Id}{extension}";
        var directory = Path.Combine(WebRoot, "avatars");
        Directory.CreateDirectory(directory);

        DeleteAvatarFile(user.AvatarPath, relativePath);

        var absolutePath = Path.Combine(WebRoot, "avatars", $"{user.Id}{extension}");
        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        user.AvatarPath = relativePath;
        await userManager.UpdateAsync(user);

        return Ok(new AvatarResponse("/" + relativePath));
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null)
        {
            return Problem(404, "not_found", "That user no longer exists.");
        }

        DeleteAvatarFile(user.AvatarPath, keep: null);
        user.AvatarPath = null;
        await userManager.UpdateAsync(user);

        return NoContent();
    }

    private string WebRoot => environment.WebRootPath
        ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    /// <summary>Removes a previously stored avatar unless it is the file just written.</summary>
    private void DeleteAvatarFile(string? avatarPath, string? keep)
    {
        if (string.IsNullOrWhiteSpace(avatarPath) ||
            string.Equals(avatarPath, keep, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var absolute = Path.Combine(WebRoot, avatarPath.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(absolute))
        {
            System.IO.File.Delete(absolute);
        }
    }
}
