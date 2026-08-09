namespace Wopcorn.Server.Data.Entities;

/// <summary>Join entity, composite key (TitleKey, GenreTmdbId).</summary>
public class TitleGenre
{
    public required string TitleKey { get; set; }
    public Title Title { get; set; } = null!;
    public int GenreTmdbId { get; set; }
    public Genre Genre { get; set; } = null!;
}
