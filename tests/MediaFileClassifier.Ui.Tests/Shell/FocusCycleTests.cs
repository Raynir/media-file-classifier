using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for the F6 order of the main areas (E16-S03-T01).
/// </summary>
public sealed class FocusCycleTests
{
    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(3, false, 0)]
    [InlineData(0, true, 3)]
    [InlineData(2, true, 1)]
    public void Next_MovesOneAreaAndWraps(int current, bool backwards, int expected)
        => Assert.Equal(expected, FocusCycle.Next([true, true, true, true], current, backwards));

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public void Next_FromOutsideTheAreas_StartsAtTheFirstOrLastArea(bool backwards, int expected)
        => Assert.Equal(expected, FocusCycle.Next([true, true, true, true], null, backwards));

    [Fact]
    public void Next_SkipsUnavailableAreas()
    {
        Assert.Equal(2, FocusCycle.Next([true, false, true, false], 0, false));
        Assert.Equal(0, FocusCycle.Next([true, false, true, false], 2, false));
        Assert.Equal(2, FocusCycle.Next([true, false, true, false], 0, true));
    }

    [Fact]
    public void Next_WithOnlyTheCurrentAreaAvailable_StaysThere()
        => Assert.Equal(1, FocusCycle.Next([false, true, false, false], 1, false));

    [Fact]
    public void Next_WithNoAreaAvailable_ReturnsNull()
        => Assert.Null(FocusCycle.Next([false, false, false, false], 1, false));
}
