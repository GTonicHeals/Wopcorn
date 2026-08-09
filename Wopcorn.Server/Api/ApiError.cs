namespace Wopcorn.Server.Api;

/// <summary>
/// The body of every non-2xx response (API-CONTRACT.md, "Error shape").
/// <paramref name="Code"/> is a stable machine string; <paramref name="Message"/>
/// is user-presentable (NFR-10).
/// </summary>
public record ApiError(string Code, string Message, IDictionary<string, string[]>? Errors = null);
