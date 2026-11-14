using MediaFileClassifier.Domain.Searches;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// One entry of the sort dropdown (E10-S04-T02).
/// </summary>
/// <param name="Mode">The sort mode.</param>
/// <param name="Text">Its name, e.g. "Date taken".</param>
public sealed record SortOptionViewModel(SortMode Mode, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}
