using System.Globalization;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Tests for <see cref="Format"/>: switching the culture changes the formatting (E13-S08-T02).
/// </summary>
public sealed class FormatTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    [Fact]
    public void Number_FollowsTheCulture()
    {
        Assert.Equal("1,234,567", Format.Number(1_234_567, English));
        Assert.Equal("1.234.567", Format.Number(1_234_567, German));
        Assert.Equal("3.50", Format.Number(3.5, 2, English));
        Assert.Equal("3,50", Format.Number(3.5, 2, German));
    }

    [Fact]
    public void Date_FollowsTheCulture()
    {
        var value = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero).ToLocalTime();

        Assert.Equal("3/4/2026", Format.Date(value, English));
        Assert.Equal("04.03.2026", Format.Date(value, German));
        Assert.NotEqual(Format.DateTime(value, English), Format.DateTime(value, German));
    }

    [Fact]
    public void Number_UsesTheCurrentCultureByDefault()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = German;
            var german = Format.Number(1_234_567);
            CultureInfo.CurrentCulture = English;
            var english = Format.Number(1_234_567);

            Assert.Equal("1.234.567", german);
            Assert.Equal("1,234,567", english);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1,023 bytes")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(15_938_355, "15.2 MB")]
    [InlineData(159_383_552, "152 MB")]
    [InlineData(5_368_709_120, "5.0 GB")]
    public void FileSize_UsesBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, Format.FileSize(bytes, English));
    }

    [Fact]
    public void FileSize_FollowsTheCulture()
    {
        Assert.Equal("1,5 KB", Format.FileSize(1536, German));
        Assert.Equal("1 023 bytes", Format.FileSize(1023, Russian));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3_725, "1:02:05")]
    [InlineData(-5, "0:00")]
    public void Duration_ShowsHoursOnlyWhenNeeded(int seconds, string expected)
    {
        Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds), English));
    }
}
