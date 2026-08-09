using Microsoft.AspNetCore.Mvc;
using Wopcorn.Server.Api;
using Wopcorn.Server.Catalog;

namespace Wopcorn.Server.Controllers;

/// <summary>
/// The cached TMDB genre list, for filter UIs. Database-first, so it answers with
/// TMDB unreachable (FR-B8).
/// </summary>
[Route("api/genres")]
public class GenresController(GenreCatalogService genres) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await genres.GetAllAsync(ct));
}
