using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wopcorn.Server.Tmdb;

namespace Wopcorn.Server.Api;

/// <summary>
/// Turns the one exception <see cref="ITmdbClient"/> raises into the contract's
/// <c>503 tmdb_unavailable</c>, once, globally — so no action needs a try/catch
/// and no upstream detail can leak into a response body (NFR-10, FR-B5).
/// </summary>
public sealed class TmdbUnavailableFilter(ILogger<TmdbUnavailableFilter> logger) : IExceptionFilter
{
    public const string Message =
        "TMDB is not responding right now. Your lists and ratings are unaffected.";

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TmdbUnavailableException)
        {
            return;
        }

        logger.LogWarning("Answering {Path} with 503: TMDB is unavailable.",
            context.HttpContext.Request.Path.Value);

        context.Result = new ObjectResult(new ApiError("tmdb_unavailable", Message))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}
