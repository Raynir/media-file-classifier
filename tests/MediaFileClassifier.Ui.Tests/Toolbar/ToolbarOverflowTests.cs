using MediaFileClassifier.Ui.Toolbar;

namespace MediaFileClassifier.Ui.Tests.Toolbar;

/// <summary>
/// Tests for which toolbar actions move into the overflow menu (E13-S03-T03).
/// </summary>
public sealed class ToolbarOverflowTests
{
    private const double More = 34;
    private const double Gap = 8;

    [Theory]
    [InlineData(316)]
    [InlineData(1000)]
    [InlineData(double.PositiveInfinity)]
    public void ActionsThatFit_StayOnTheToolbar(double available)
    {
        // 100 + 8 + 100 + 8 + 100 = 316.
        Assert.Equal([false, false, false], ToolbarOverflow.Fit(available, [100, 100, 100], [1, 2, 3], More, Gap));
    }

    [Fact]
    public void LowestPriority_MovesFirst_AndTheMoreButtonGetsRoomToo()
    {
        // 315 doesn't fit all three; two (208) plus the More button (8 + 34) take 250.
        Assert.Equal([false, true, false], ToolbarOverflow.Fit(315, [100, 100, 100], [3, 1, 2], More, Gap));
    }

    [Fact]
    public void EqualPriorities_MoveFromTheRight()
    {
        Assert.Equal([false, false, true], ToolbarOverflow.Fit(300, [100, 100, 100], [5, 5, 5], More, Gap));
    }

    [Fact]
    public void ActionsMove_UntilTheRestFitBesideTheMoreButton()
    {
        // One action (100) and the More button (8 + 34) need 142.
        Assert.Equal([false, true, true], ToolbarOverflow.Fit(142, [100, 100, 100], [3, 2, 1], More, Gap));
        Assert.Equal([true, true, true], ToolbarOverflow.Fit(141, [100, 100, 100], [3, 2, 1], More, Gap));
    }

    [Fact]
    public void NoRoomAtAll_PutsEveryActionInTheMenu()
    {
        Assert.Equal([true, true], ToolbarOverflow.Fit(0, [100, 100], [1, 2], More, Gap));
    }

    [Fact]
    public void NoActions_NothingToMove()
    {
        Assert.Empty(ToolbarOverflow.Fit(0, [], [], More, Gap));
    }
}
