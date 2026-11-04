using System.Collections.ObjectModel;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.StatusBar;

/// <summary>
/// The status bar at the bottom of the main window (E13-S04): segments that features add through
/// <see cref="IStatusBarContributor"/>, so a new segment never needs a change here.
/// </summary>
public sealed class StatusBarViewModel : ViewModelBase
{
    private readonly ObservableCollection<StatusBarSegment> left = [];
    private readonly ObservableCollection<StatusBarSegment> right = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarViewModel"/> class and collects the segments.
    /// </summary>
    /// <param name="contributors">The features that add segments.</param>
    public StatusBarViewModel(IEnumerable<IStatusBarContributor> contributors)
    {
        Left = new ReadOnlyObservableCollection<StatusBarSegment>(left);
        Right = new ReadOnlyObservableCollection<StatusBarSegment>(right);
        foreach (var contributor in contributors)
        {
            contributor.Register(this);
        }
    }

    /// <summary>
    /// Gets the segments at the left end, from the left edge inwards.
    /// </summary>
    public ReadOnlyObservableCollection<StatusBarSegment> Left { get; }

    /// <summary>
    /// Gets the segments at the right end, from the inside out to the right edge, i.e. in display order.
    /// </summary>
    public ReadOnlyObservableCollection<StatusBarSegment> Right { get; }

    /// <summary>
    /// Adds a segment at its place.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The same segment.</returns>
    /// <exception cref="InvalidOperationException">Another segment has the same ID.</exception>
    public StatusBarSegment Add(StatusBarSegment segment)
    {
        if (left.Concat(right).Any(existing => existing.Id == segment.Id))
        {
            throw new InvalidOperationException($"A status bar segment with the ID '{segment.Id}' is already registered.");
        }

        if (segment.Side == StatusBarSide.Left)
        {
            left.Insert(left.Count(existing => existing.Order <= segment.Order), segment);
        }
        else
        {
            // Right-side orders count from the right edge, so the lowest order is displayed last.
            right.Insert(right.Count(existing => existing.Order > segment.Order), segment);
        }

        return segment;
    }
}
