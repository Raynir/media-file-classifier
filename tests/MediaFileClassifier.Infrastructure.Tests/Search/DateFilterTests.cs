using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The date filter (E10-S03-T02): every preset at the turn of a year, custom ranges, the fallback dates, and the
/// viewer's time zone (UTC+3 here, so UTC and local days differ).
/// </summary>
public sealed class DateFilterTests(DateFilterTests.Library library) : IClassFixture<DateFilterTests.Library>
{
    private static readonly TimeZoneInfo Plus3 = TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");

    // UTC+1, with daylight saving time (+2) from 00:00 on the last Sunday of March to 00:00 (DST) on the last Sunday
    // of October, so midnight is skipped in spring and repeated in autumn.
    private static readonly TimeZoneInfo MidnightDst = TimeZoneInfo.CreateCustomTimeZone(
        "Test DST",
        TimeSpan.FromHours(1),
        "Test DST",
        "Test standard",
        "Test daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string[]> PresetCases() => new()
    {
        // Half an hour into the new year.
        { "2027-01-01 00:30", DateFilter.Today, ["B", "C"] },
        { "2027-01-01 00:30", DateFilter.Last7Days, ["A", "B", "C", "D", "F"] },
        { "2027-01-01 00:30", DateFilter.Last30Days, ["A", "B", "C", "D", "E", "F"] },
        { "2027-01-01 00:30", DateFilter.ThisYear, ["B", "C"] },
        { "2027-01-01 00:30", DateFilter.LastYear, ["A", "D", "E", "F"] },

        // A minute before the new year.
        { "2026-12-31 23:59", DateFilter.Today, ["A", "D"] },
        { "2026-12-31 23:59", DateFilter.Last7Days, ["A", "D", "E", "F"] },
        { "2026-12-31 23:59", DateFilter.Last30Days, ["A", "D", "E", "F"] },
        { "2026-12-31 23:59", DateFilter.ThisYear, ["A", "D", "E", "F"] },
        { "2026-12-31 23:59", DateFilter.LastYear, ["G"] },
        // An unknown preset, e.g. from a newer version.
        { "2027-01-01 00:30", "next-century", [] },
    };

    public static TheoryData<DateTimeOffset?, DateTimeOffset?, string[]> CustomCases() => new()
    {
        // The clock times count, not the offsets they carry.
        { new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.FromHours(3)), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)), ["A", "D"] },
        { new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), ["A", "D"] },
        { new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), null, ["B", "C"] },
        { null, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ["G"] },
        { new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 26, 0, 0, 1, TimeSpan.Zero), ["F"] },
        { new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), [] },
    };

    [Theory]
    [MemberData(nameof(PresetCases))]
    public async Task Preset_KeepsTheItemsOfItsLocalDays(string now, string preset, string[] expected)
    {
        var names = await library.SearchAsync(Clock(now), new DateRangeValue(preset, null, null));

        Assert.Equal(expected, names);
    }

    [Theory]
    [MemberData(nameof(CustomCases))]
    public async Task CustomRange_KeepsTheItemsFromItsStartToItsEnd(DateTimeOffset? from, DateTimeOffset? to, string[] expected)
    {
        var names = await library.SearchAsync(Clock("2027-01-01 00:30"), new DateRangeValue(null, from, to));

        Assert.Equal(expected, names);
    }

    [Fact]
    public async Task Preset_WinsOverACustomRange()
    {
        var names = await library.SearchAsync(Clock("2027-01-01 00:30"), new DateRangeValue(DateFilter.Today, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null));

        Assert.Equal(["B", "C"], names);
    }

    [Fact]
    public void Presets_ListThePresets()
    {
        var options = new DateFilter(TimeProvider.System).Presets;

        Assert.Equal(
            [DateFilter.Today, DateFilter.Last7Days, DateFilter.Last30Days, DateFilter.ThisYear, DateFilter.LastYear],
            options.Select(option => option.Id));
        Assert.All(options, option => Assert.StartsWith("Filter_Date_", option.DisplayNameKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026-12-31 23:59", "2026-12-25", "2027-01-01")]
    [InlineData("2027-02-28 10:00", "2027-02-22", "2027-03-01")]
    [InlineData("2028-03-01 10:00", "2028-02-24", "2028-03-02")]
    public void Last7Days_EndsTomorrowAndCountsToday(string now, string from, string to)
    {
        var range = DateFilter.Resolve(new DateRangeValue(DateFilter.Last7Days, null, null), DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal((DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture)), range);
    }

    [Fact]
    public void ToInstant_SkippedMidnight_IsTheChange()
    {
        // 2027-03-28: the clocks go from 00:00 (+1) to 01:00 (+2), so the day starts at 23:00 UTC the day before.
        Assert.Equal(new DateTimeOffset(2027, 3, 27, 23, 0, 0, TimeSpan.Zero), DateFilter.ToInstant(new DateTime(2027, 3, 28), MidnightDst));
        Assert.Equal(new DateTimeOffset(2027, 3, 27, 23, 0, 0, TimeSpan.Zero), DateFilter.ToInstant(new DateTime(2027, 3, 28, 1, 0, 0), MidnightDst));
        Assert.Equal(new DateTimeOffset(2027, 3, 28, 22, 0, 0, TimeSpan.Zero), DateFilter.ToInstant(new DateTime(2027, 3, 29), MidnightDst));
    }

    [Fact]
    public void ToInstant_RepeatedMidnight_IsTheFirst()
    {
        // 2027-10-31: the clocks go from 01:00 (+2) back to 00:00 (+1), so midnight happens twice; the day starts at the first.
        Assert.Equal(new DateTimeOffset(2027, 10, 30, 22, 0, 0, TimeSpan.Zero), DateFilter.ToInstant(new DateTime(2027, 10, 31), MidnightDst));
        Assert.Equal(new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.Zero), DateFilter.ToInstant(new DateTime(2027, 10, 31, 1, 0, 0), MidnightDst));
    }

    [Fact]
    public void ToSql_PassesEveryValueAsAParameter()
    {
        var sql = new Infrastructure.DataAccess.Search.SearchSql();

        var condition = new DateFilter(Clock("2027-01-01 00:30")).ToSql(new DateRangeValue(DateFilter.ThisYear, null, null), sql);

        Assert.NotNull(condition);
        Assert.DoesNotContain("2027", condition, StringComparison.Ordinal);
        Assert.Equal(4, sql.Parameters.Count);
        Assert.True(sql.NeedsMediaItem);
        Assert.True(sql.NeedsTechnicalMetadata);
    }

    private static ZonedClock Clock(string localNow)
    {
        var clock = DateTime.Parse(localNow, System.Globalization.CultureInfo.InvariantCulture);
        return new ZonedClock(new DateTimeOffset(clock, Plus3.GetUtcOffset(clock)), Plus3);
    }

    /// <summary>
    /// A fixed clock in a given time zone.
    /// </summary>
    private sealed class ZonedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;

        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    /// <summary>
    /// Items around the turn of 2026 to 2027, seen from UTC+3. Their file names are the letters the cases name.
    /// </summary>
    public sealed class Library : IAsyncLifetime
    {
        private readonly Dictionary<Id<MediaItem>, string> names = [];

        private SearchTestBed bed = null!;

        public async Task<string[]> SearchAsync(TimeProvider clock, DateRangeValue value)
        {
            var filter = new SearchFilter { Conditions = [new FilterCondition(new DateFilter(clock), value)] };
            var results = await bed.SearchAsync(new SearchQuery { Filter = filter });
            return [.. results.Hits.Select(hit => names[hit.ItemId]).Order(StringComparer.Ordinal)];
        }

        public async ValueTask InitializeAsync()
        {
            bed = await SearchTestBed.CreateAsync();

            // A: taken 23:30 on 31 December, offset unknown: a local time, so in 2026.
            await AddAsync("A", Taken(new DateTime(2026, 12, 31, 23, 30, 0), null));

            // B: taken 00:10 on 1 January, offset unknown.
            await AddAsync("B", Taken(new DateTime(2027, 1, 1, 0, 10, 0), null));

            // C: a video created 22:30 UTC on 31 December, which is 01:30 on 1 January in UTC+3.
            await AddAsync("C", Taken(new DateTime(2026, 12, 31, 22, 30, 0), TimeSpan.Zero, DateTakenSource.VideoCreationTime));

            // D: no technical metadata yet: the file was modified 20:59 UTC on 31 December, 23:59 local.
            await AddAsync("D", null, new DateTimeOffset(2026, 12, 31, 20, 59, 0, TimeSpan.Zero));

            // E: the file modification time as the date taken: 10:00 UTC on 25 December, 13:00 local.
            await AddAsync("E", Taken(new DateTime(2026, 12, 25, 10, 0, 0), TimeSpan.Zero, DateTakenSource.FileModified));

            // F: taken at midnight starting 26 December, offset unknown.
            await AddAsync("F", Taken(new DateTime(2026, 12, 26), null));

            // G: taken a second before 2026 in Tokyo; local time there counts, so in 2025.
            await AddAsync("G", Taken(new DateTime(2025, 12, 31, 23, 59, 59), null));
        }

        public ValueTask DisposeAsync() => bed.DisposeAsync();

        private static (DateTime Taken, TimeSpan? Offset, DateTakenSource Source) Taken(DateTime taken, TimeSpan? offset, DateTakenSource source = DateTakenSource.ExifDateTimeOriginal) =>
            (taken, offset, source);

        private async Task AddAsync(string name, (DateTime Taken, TimeSpan? Offset, DateTakenSource Source)? date, DateTimeOffset? modified = null)
        {
            var item = MediaItem.Discover(
                bed.Library.Folder.Id,
                RelativePath.From(name + ".jpg"),
                MediaType.Image,
                1000,
                LibraryTestBed.Start,
                modified ?? LibraryTestBed.Start,
                (UInt128)names.Count + 1,
                LibraryTestBed.Start);
            await bed.Library.Library.Items.AddAsync(item, TestContext.Current.CancellationToken);
            if (date is { } value)
            {
                await bed.Library.Library.Items.UpdateAsync(
                    item,
                    new TechnicalMetadata
                    {
                        ItemId = item.Id,
                        Width = 100,
                        Height = 100,
                        DateTaken = value.Taken,
                        DateTakenOffset = value.Offset,
                        DateTakenSource = value.Source,
                        ExtractedAt = LibraryTestBed.Start,
                    },
                    TestContext.Current.CancellationToken);
            }

            names[item.Id] = name;
        }
    }
}
