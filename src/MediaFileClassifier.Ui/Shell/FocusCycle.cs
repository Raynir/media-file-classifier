namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The order F6 and Shift+F6 move the focus through the main areas (E16-S03): navigation, toolbar, browser, details, status bar.
/// </summary>
public static class FocusCycle
{
    /// <summary>
    /// Finds the area the focus moves to.
    /// </summary>
    /// <param name="available">For each area in cycle order, whether it can take the focus now (shown, not hidden).</param>
    /// <param name="current">The area that has the focus, or <see langword="null"/> when it is outside them, such as in the menu.</param>
    /// <param name="backwards">Whether the move is Shift+F6.</param>
    /// <returns>The index of the next available area, or <see langword="null"/> when none is.</returns>
    public static int? Next(IReadOnlyList<bool> available, int? current, bool backwards)
    {
        var count = available.Count;
        var step = backwards ? count - 1 : 1;

        // From outside the areas, forwards starts at the first and backwards at the last.
        var from = current ?? (backwards ? 0 : count - 1);
        for (var i = 1; i <= count; i++)
        {
            var candidate = (from + (i * step)) % count;
            if (available[candidate])
            {
                return candidate;
            }
        }

        return null;
    }
}
