namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// Decides which toolbar actions move into the overflow menu (E13-S03-T03).
/// </summary>
public static class ToolbarOverflow
{
    /// <summary>
    /// Fits the actions into a width. When they all fit, none moves. Otherwise the overflow button takes its place
    /// after them, and actions move into the menu until the rest fit beside it: the lowest priority first, and
    /// of equal priorities the rightmost first.
    /// </summary>
    /// <param name="available">The width the actions may take.</param>
    /// <param name="widths">The width of each action button, left to right.</param>
    /// <param name="priorities">The priority of each action, in the same order.</param>
    /// <param name="overflowButtonWidth">The width of the overflow button.</param>
    /// <param name="spacing">The space between two buttons.</param>
    /// <returns>For each action, whether it moves into the overflow menu.</returns>
    public static bool[] Fit(double available, IReadOnlyList<double> widths, IReadOnlyList<int> priorities, double overflowButtonWidth, double spacing)
    {
        var overflowed = new bool[widths.Count];
        if (double.IsPositiveInfinity(available) || RowWidth(widths, overflowed, spacing) <= available)
        {
            return overflowed;
        }

        var budget = available - overflowButtonWidth - spacing;
        var byPriority = Enumerable.Range(0, widths.Count).OrderBy(index => priorities[index]).ThenByDescending(index => index);
        foreach (var index in byPriority)
        {
            if (RowWidth(widths, overflowed, spacing) <= budget)
            {
                break;
            }

            overflowed[index] = true;
        }

        return overflowed;
    }

    private static double RowWidth(IReadOnlyList<double> widths, bool[] overflowed, double spacing)
    {
        var kept = Enumerable.Range(0, widths.Count).Where(index => !overflowed[index]).ToList();
        return kept.Count == 0 ? 0 : kept.Sum(index => widths[index]) + ((kept.Count - 1) * spacing);
    }
}
