using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Wopcorn.Server.Data;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as its UTC tick count.
///
/// SQLite's EF provider refuses <c>ORDER BY</c> on a <c>DateTimeOffset</c>
/// column — it is persisted as text carrying an offset, which does not sort
/// chronologically once offsets differ. be-03's default <c>added</c> sort and
/// be-04's keyset feed both order by an instant, so the column has to be
/// sortable.
///
/// A <c>long</c> of UTC ticks sorts correctly on every relational provider,
/// keeps full precision (unlike EF's built-in binary converter, which quantises
/// to 100µs), and loses nothing: every timestamp Wopcorn writes comes from
/// <see cref="DateTimeOffset.UtcNow"/>, and the contract states dates are
/// ISO-8601 UTC (NFR-7).
/// </summary>
public sealed class UtcInstantConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
