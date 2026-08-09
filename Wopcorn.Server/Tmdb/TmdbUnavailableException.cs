namespace Wopcorn.Server.Tmdb;

/// <summary>
/// The single failure surface of <see cref="ITmdbClient"/>: timeout, network
/// error, rate-limit exhaustion, or any non-404 error status. Never carries a
/// URL, a header, or a credential — the message is surfaced to the client by
/// <c>TmdbUnavailableFilter</c> (FR-B5, NFR-10).
/// </summary>
public sealed class TmdbUnavailableException : Exception
{
    public TmdbUnavailableException(string message) : base(message)
    {
    }

    public TmdbUnavailableException(string message, Exception? inner) : base(message, inner)
    {
    }
}
