namespace Wopcorn.Server.Tmdb;

/// <summary>
/// Bound from the <c>Tmdb</c> configuration section. The credentials live in .NET
/// user secrets and are never written to appsettings (FR-B5). Nothing outside
/// <see cref="TmdbClient"/> may read them.
/// </summary>
public class TmdbOptions
{
    public const string Section = "Tmdb";

    /// <summary>v4 bearer token, <c>api_read</c> scope. Preferred.</summary>
    public string? ReadAccessToken { get; set; }

    /// <summary>v3 API key. Fallback when no bearer token is configured.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";

    public string Language { get; set; } = "en-US";
}
