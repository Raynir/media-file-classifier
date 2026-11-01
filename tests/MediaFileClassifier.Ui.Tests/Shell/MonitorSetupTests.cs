using System.Globalization;
using Avalonia;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for monitor setup fingerprints and the off-screen check (E13-S01-T03).
/// </summary>
public sealed class MonitorSetupTests
{
    private static readonly PixelRect Primary = new(0, 0, 2560, 1400);
    private static readonly PixelRect Secondary = new(2560, 0, 1920, 1040);

    [Fact]
    public void Fingerprint_IgnoresScreenOrderAndCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            var fingerprint = MonitorSetup.Fingerprint([(Secondary, 1.0), (Primary, 1.5)]);

            Assert.Equal("0,0,2560x1400@1.5|2560,0,1920x1040@1", fingerprint);
            Assert.Equal(fingerprint, MonitorSetup.Fingerprint([(Primary, 1.5), (Secondary, 1.0)]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Fingerprint_ChangesWithScaling()
        => Assert.NotEqual(MonitorSetup.Fingerprint([(Primary, 1.0)]), MonitorSetup.Fingerprint([(Primary, 1.25)]));

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(3000, 100, true)]
    [InlineData(2500, 100, true)]
    [InlineData(5000, 100, false)]
    [InlineData(100, 1390, false)]
    [InlineData(-1250, 100, false)]
    [InlineData(-1100, 100, true)]
    public void IsReachable_NeedsPartOfTheTitleBarOnAScreen(int x, int y, bool expected)
    {
        var placement = new WindowPlacement(x, y, 1200, 800, IsMaximized: false);

        Assert.Equal(expected, MonitorSetup.IsReachable(placement, 1.0, [Primary, Secondary]));
    }

    [Fact]
    public void IsReachable_AfterTheSecondMonitorIsGone_IsFalse()
    {
        var onSecondary = new WindowPlacement(2800, 100, 1200, 800, IsMaximized: false);

        Assert.True(MonitorSetup.IsReachable(onSecondary, 1.0, [Primary, Secondary]));
        Assert.False(MonitorSetup.IsReachable(onSecondary, 1.0, [Primary]));
    }
}
