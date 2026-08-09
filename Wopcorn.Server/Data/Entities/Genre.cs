namespace Wopcorn.Server.Data.Entities;

/// <summary>
/// The mirrored TMDB genre catalog — the <b>union</b> of <c>/genre/movie/list</c>
/// and <c>/genre/tv/list</c>. The ids overlap where the names match (Drama 18,
/// Comedy 35) and TV adds its own (Action &amp; Adventure 10759, Kids 10762,
/// 10763–10768), so the union is conflict-free.
/// </summary>
public class Genre
{
    public int TmdbId { get; set; }
    public required string Name { get; set; }

    /// <summary>Set when TMDB lists this genre for films.</summary>
    public bool InMovies { get; set; }

    /// <summary>Set when TMDB lists this genre for TV.</summary>
    public bool InTv { get; set; }

    /// <summary>
    /// The contract's <c>mediaTypes</c>. A TV genre covers seasons as well as
    /// series, because a season's genres are its series' genres — TMDB season
    /// objects carry none of their own.
    /// </summary>
    public IReadOnlyList<MediaType> MediaTypes()
    {
        var types = new List<MediaType>(3);
        if (InMovies)
        {
            types.Add(MediaType.Movie);
        }

        if (InTv)
        {
            types.Add(MediaType.Series);
            types.Add(MediaType.Season);
        }

        return types;
    }
}
