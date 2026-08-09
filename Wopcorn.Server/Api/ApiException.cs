namespace Wopcorn.Server.Api;

/// <summary>
/// An outcome a service decided but only the pipeline can express: the status and
/// the contract's <see cref="ApiError"/> code, raised from deep inside a service
/// so no action needs a try/catch and no controller can accidentally answer with a
/// different code than the one the rule intends.
///
/// <see cref="ApiExceptionFilter"/> turns it into the response.
/// </summary>
public class ApiException(
    int status,
    string code,
    string message,
    IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    /// <summary>
    /// The contract's per-field map, which only <c>validation_failed</c> carries.
    /// Null everywhere else, and absent from the response rather than empty.
    /// </summary>
    public IDictionary<string, string[]>? Errors { get; } = errors;
}

/// <summary>
/// <c>403 forbidden</c> — the caller is not a friend, or is not the party a rule
/// names (NFR-4, FR-F6). Thrown by <c>FriendshipService.RequireFriendshipAsync</c>
/// and by accept/decline when someone other than the recipient acts.
/// </summary>
public sealed class ForbiddenException(string message)
    : ApiException(StatusCodes.Status403Forbidden, "forbidden", message)
{
    public const string NotFriends = "You can only see this for people you are friends with.";
}
