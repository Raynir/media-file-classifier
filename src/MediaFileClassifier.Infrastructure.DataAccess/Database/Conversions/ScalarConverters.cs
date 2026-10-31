using System.Globalization;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Stores an <see cref="Id{TEntity}"/> as its canonical 36-character lowercase text. UUIDv7 text sorts in creation
/// order (D066, D071).
/// </summary>
/// <typeparam name="TEntity">Entity the ID belongs to.</typeparam>
internal sealed class IdConverter<TEntity>()
    : ValueConverter<Id<TEntity>, string>(id => id.Value.ToString(), text => new Id<TEntity>(Guid.Parse(text)));

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as UTC Unix milliseconds, so it sorts and compares in SQL (D071).
/// The offset isn't kept; loaded values are UTC.
/// </summary>
internal sealed class UnixMillisecondsConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeMilliseconds(),
        milliseconds => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));

/// <summary>
/// Stores a <see cref="DateTime"/> without a time zone, such as a camera's local "date taken", as milliseconds since
/// the Unix epoch of the same wall-clock time. Loaded values have <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
internal sealed class WallClockMillisecondsConverter()
    : ValueConverter<DateTime, long>(
        value => (value.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond,
        milliseconds => new DateTime(DateTime.UnixEpoch.Ticks + (milliseconds * TimeSpan.TicksPerMillisecond), DateTimeKind.Unspecified));

/// <summary>
/// Stores a <see cref="TimeSpan"/> as whole milliseconds.
/// </summary>
internal sealed class TimeSpanMillisecondsConverter()
    : ValueConverter<TimeSpan, long>(
        value => value.Ticks / TimeSpan.TicksPerMillisecond,
        milliseconds => TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond));

/// <summary>
/// Stores a 128-bit hash as 32 lowercase hex digits, which SQLite can index and compare.
/// </summary>
internal sealed class UInt128HexConverter()
    : ValueConverter<UInt128, string>(
        value => value.ToString("x32", CultureInfo.InvariantCulture),
        text => UInt128.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));

/// <summary>
/// Stores a 64-bit hash in SQLite's signed INTEGER with the same bits.
/// </summary>
internal sealed class UInt64BitsConverter()
    : ValueConverter<ulong, long>(value => unchecked((long)value), bits => unchecked((ulong)bits));

/// <summary>
/// Stores a <see cref="RelativePath"/> as its <c>/</c>-separated text.
/// </summary>
internal sealed class RelativePathConverter()
    : ValueConverter<RelativePath, string>(path => path.Value, text => RelativePath.From(text));

/// <summary>
/// Stores a <see cref="TagName"/> as its display text. Tags are matched by their <c>tag_key</c> column instead,
/// see <see cref="TagKeyGenerator"/>.
/// </summary>
internal sealed class TagNameConverter()
    : ValueConverter<TagName, string>(tag => tag.Value, text => TagName.From(text));
