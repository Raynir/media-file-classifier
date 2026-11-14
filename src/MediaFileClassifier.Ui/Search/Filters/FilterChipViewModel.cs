using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A chip summarising one filter in force, e.g. "Type: Video" (E10-S02-T02). Equal chips (same filter and text) are
/// reused when the bar refreshes, so a chip with the keyboard focus keeps it.
/// </summary>
public sealed partial class FilterChipViewModel : ViewModelBase, IEquatable<FilterChipViewModel>
{
    private readonly Action remove;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterChipViewModel"/> class.
    /// </summary>
    /// <param name="filterId">The filter's ID.</param>
    /// <param name="text">The summary shown.</param>
    /// <param name="removeLabel">The accessible name of the remove button.</param>
    /// <param name="remove">Removes the filter.</param>
    public FilterChipViewModel(string filterId, string text, string removeLabel, Action remove)
    {
        FilterId = filterId;
        Text = text;
        RemoveLabel = removeLabel;
        this.remove = remove;
    }

    /// <summary>
    /// Gets the filter's ID.
    /// </summary>
    public string FilterId { get; }

    /// <summary>
    /// Gets the summary shown.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the accessible name of the remove button.
    /// </summary>
    public string RemoveLabel { get; }

    /// <summary>
    /// Removes the filter.
    /// </summary>
    [RelayCommand]
    public void Remove() => remove();

    /// <inheritdoc />
    public bool Equals(FilterChipViewModel? other) => other is not null && FilterId == other.FilterId && Text == other.Text;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FilterChipViewModel);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FilterId, Text);
}
