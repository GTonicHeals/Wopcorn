using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Wopcorn.Server.Api;

/// <summary>
/// Turns an <see cref="ApiException"/> into its status and the contract's error
/// body, globally — the same job <see cref="TmdbUnavailableFilter"/> does for the
/// one exception <c>ITmdbClient</c> raises, for the ones the social services
/// raise (NFR-4, NFR-10).
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ApiException api)
        {
            return;
        }

        context.Result = new ObjectResult(new ApiError(api.Code, api.Message, api.Errors))
        {
            StatusCode = api.Status,
        };
        context.ExceptionHandled = true;
    }
}
