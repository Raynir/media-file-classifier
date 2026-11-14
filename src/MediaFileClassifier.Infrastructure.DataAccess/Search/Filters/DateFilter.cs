using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The date filter (E10-S03): the date taken, else the fallback dates of E03-S03-T04 (video creation time, file
/// modification time), compared in the viewer's local calendar. A date taken with an unknown offset (D105) is a local
/// clock time already and compares as it is; every other date is an instant and compares with the instants at which the
/// range's local days start, so a photo taken at 23:30 on 31 December is in that year.
/// <para>
/// A preset is resolved when the search runs (today's date from <see cref="TimeProvider"/>), so a saved search stays
/// relative; an unknown preset matches nothing. A custom range uses the clock times of <see cref="DateRangeValue.From"/>
/// (inclusive) and <see cref="DateRangeValue.To"/> (exclusive) as local times, whatever offset they carry.
/// </para>
/// </summary>
/// <param name="time">The clock and the local time zone.</param>
internal sealed class DateFilter(TimeProvider time) : FilterDefinition<DateRangeValue>, IFilterPresets
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "date";

    /// <summary>
    /// The preset of today.
    /// </summary>
    public const string Today = "today";

    /// <summary>
    /// The preset of the last 7 calendar days, today included.
    /// </summary>
    public const string Last7Days = "last-7-days";

    /// <summary>
    /// The preset of the last 30 calendar days, today included.
    /// </summary>
    public const string Last30Days = "last-30-days";

    /// <summary>
    /// The preset of this calendar year.
    /// </summary>
    public const string ThisYear = "this-year";

    /// <summary>
    /// The preset of the previous calendar year.
    /// </summary>
    public const string LastYear = "last-year";

    private static readonly IReadOnlyList<FilterPreset> PresetList =
    [
        new(Today, "Filter_Date_Today"),
        new(Last7Days, "Filter_Date_Last7Days"),
        new(Last30Days, "Filter_Date_Last30Days"),
        new(ThisYear, "Filter_Date_ThisYear"),
        new(LastYear, "Filter_Date_LastYear"),
    ];

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Date";

    /// <inheritdoc />
    public override int Order => 300;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.DateRange;

    /// <inheritdoc />
    public IReadOnlyList<FilterPreset> Presets => PresetList;

    /// <summary>
    /// Resolves a value to local clock times.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="today">Today's local date.</param>
    /// <returns>The start (inclusive) and end (exclusive), either open; <see langword="null"/> for an unknown preset.</returns>
    internal static (DateTime? From, DateTime? To)? Resolve(DateRangeValue value, DateTime today)
    {
        today = today.Date;
        if (string.IsNullOrWhiteSpace(value.Preset))
        {
            return (value.From?.DateTime, value.To?.DateTime);
        }

        return value.Preset switch
        {
            Today => (today, today.AddDays(1)),
            Last7Days => (today.AddDays(-6), today.AddDays(1)),
            Last30Days => (today.AddDays(-29), today.AddDays(1)),
            ThisYear => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 1, 1).AddYears(1)),
            LastYear => (new DateTime(today.Year, 1, 1).AddYears(-1), new DateTime(today.Year, 1, 1)),
            _ => null,
        };
    }

    /// <summary>
    /// Finds the instant a local clock time happens at. A time skipped by a daylight saving change maps to the instant
    /// of the change, and a repeated time to its first occurrence, so a day starts at its first instant.
    /// </summary>
    /// <param name="clock">The local clock time.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The instant.</returns>
    internal static DateTimeOffset ToInstant(DateTime clock, TimeZoneInfo zone)
    {
        clock = DateTime.SpecifyKind(clock, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(clock))
        {
            // In a gap the clocks moved forward: with the offset from before the gap, the time maps to the change.
            offset = zone.GetUtcOffset(clock.AddDays(-1));
        }
        else if (zone.IsAmbiguousTime(clock))
        {
            offset = zone.GetAmbiguousTimeOffsets(clock).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(clock);
        }

        return new DateTimeOffset(clock.Ticks - offset.Ticks, TimeSpan.Zero);
    }

    /// <inheritdoc />
    protected override string? ToSql(DateRangeValue value, IFilterSql sql)
    {
        var zone = time.LocalTimeZone;
        if (Resolve(value, time.GetLocalNow().DateTime) is not { } range)
        {
            return "0";
        }

        var (from, to) = range;
        var dateTaken = sql.TechnicalMetadata("date_taken");
        var offset = sql.TechnicalMetadata("date_taken_offset");

        // Dates taken are stored as wall-clock milliseconds, offsets as milliseconds and other times as Unix
        // milliseconds (D071).
        var instant = $"coalesce({dateTaken} - {offset}, {sql.MediaItem("file_modified_at")})";
        var clock = new List<string> { $"{dateTaken} IS NOT NULL", $"{offset} IS NULL" };
        var exact = new List<string> { $"({dateTaken} IS NULL OR {offset} IS NOT NULL)" };

        if (from is { } start)
        {
            clock.Add($"{dateTaken} >= {sql.Parameter(WallClockMilliseconds(start))}");
            exact.Add($"{instant} >= {sql.Parameter(ToInstant(start, zone).ToUnixTimeMilliseconds())}");
        }

        if (to is { } end)
        {
            clock.Add($"{dateTaken} < {sql.Parameter(WallClockMilliseconds(end))}");
            exact.Add($"{instant} < {sql.Parameter(ToInstant(end, zone).ToUnixTimeMilliseconds())}");
        }

        return $"({string.Join(" AND ", clock)}) OR ({string.Join(" AND ", exact)})";
    }

    // The same as the library stores a wall-clock date taken.
    private static long WallClockMilliseconds(DateTime clock) => (clock.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
}
