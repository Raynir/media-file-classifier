using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Tests.StatusBar;

/// <summary>
/// Tests for the status bar segments framework (E13-S04-T01).
/// </summary>
public sealed class StatusBarViewModelTests
{
    [Fact]
    public void Segments_FromContributors_AreOrderedFromEachEnd()
    {
        var statusBar = new StatusBarViewModel(
        [
            new Contributor(Segment("provider", StatusBarSide.Right, 20), Segment("counts", StatusBarSide.Left, 10)),
            new Contributor(Segment("processing", StatusBarSide.Right, 10), Segment("summary", StatusBarSide.Left, 5)),
            new Contributor(Segment("offline", StatusBarSide.Right, 15)),
        ]);

        Assert.Equal(["summary", "counts"], statusBar.Left.Select(segment => segment.Id));

        // Displayed left to right, so the lowest order is at the right edge.
        Assert.Equal(["provider", "offline", "processing"], statusBar.Right.Select(segment => segment.Id));
    }

    [Fact]
    public void Add_RejectsADuplicateId()
    {
        var statusBar = new StatusBarViewModel([]);
        statusBar.Add(new("counts", StatusBarSide.Left, 0));

        Assert.Throws<InvalidOperationException>(() => statusBar.Add(new("counts", StatusBarSide.Right, 0)));
    }

    [Fact]
    public void Segment_IsClickableOnlyWithACommand_AndNamedByItsTooltip()
    {
        var segment = new StatusBarSegment("offline", StatusBarSide.Right, 0) { Text = "1 folder offline" };

        Assert.False(segment.IsClickable);
        Assert.Equal("1 folder offline", segment.AccessibleName);

        segment.Command = new RelayCommand(() => { });
        segment.ToolTip = "Open folder management";

        Assert.True(segment.IsClickable);
        Assert.Equal("Open folder management", segment.AccessibleName);
    }

    private static StatusBarSegment Segment(string id, StatusBarSide side, int order) => new(id, side, order);

    private sealed class Contributor(params StatusBarSegment[] segments) : IStatusBarContributor
    {
        public void Register(StatusBarViewModel statusBar)
        {
            foreach (var segment in segments)
            {
                statusBar.Add(segment);
            }
        }
    }
}
