using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// One column of the list view (E14-S02): its header, width and whether it shows. The list's rows and header lay out
/// their cells from these (<see cref="ListColumnsViewModel"/>).
/// </summary>
public sealed partial class ListColumnViewModel : ObservableObject
{
    /// <summary>
    /// The narrowest a column can be dragged.
    /// </summary>
    public const double MinWidth = 40;

    /// <summary>
    /// The widest a column can be dragged.
    /// </summary>
    public const double MaxWidth = 2000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListColumnViewModel"/> class.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <param name="width">The width, in device-independent pixels.</param>
    /// <param name="isVisible">Whether it shows.</param>
    public ListColumnViewModel(ListColumnKind kind, double width, bool isVisible)
    {
        Kind = kind;
        Width = width;
        IsVisible = isVisible;
    }

    /// <summary>
    /// Gets the column.
    /// </summary>
    public ListColumnKind Kind { get; }

    /// <summary>
    /// Gets the header text, empty for the thumbnail and favorite columns, which show none.
    /// </summary>
    public string Header => Kind is ListColumnKind.Thumbnail or ListColumnKind.Favorite ? string.Empty : Name;

    /// <summary>
    /// Gets a value indicating whether the header shows a star icon instead of text (the favorite column).
    /// </summary>
    public bool HasHeaderIcon => Kind == ListColumnKind.Favorite;

    /// <summary>
    /// Gets the column's name, for the show and hide menu and the header's accessible name.
    /// </summary>
    public string Name => Localizer.Current[$"List_Column_{Kind}"];

    /// <summary>
    /// Gets a value indicating whether the column can be hidden, moved and resized: all but the thumbnail.
    /// </summary>
    public bool IsConfigurable => Kind != ListColumnKind.Thumbnail;

    /// <summary>
    /// Gets a value indicating whether the column can be hidden: not the thumbnail or the name.
    /// </summary>
    public bool CanHide => Kind is not (ListColumnKind.Thumbnail or ListColumnKind.Name);

    /// <summary>
    /// Gets the sort mode a header click sorts by (E10-S04), or <see langword="null"/> when the column isn't sortable.
    /// </summary>
    public SortMode? SortMode => Kind switch
    {
        ListColumnKind.Name => Domain.Searches.SortMode.Name,
        ListColumnKind.DateTaken => Domain.Searches.SortMode.Date,
        ListColumnKind.Size => Domain.Searches.SortMode.Size,
        _ => null,
    };

    /// <summary>
    /// Gets a value indicating whether a header click sorts by the column.
    /// </summary>
    public bool IsSortable => SortMode is not null;

    /// <summary>
    /// Gets or sets the width, in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the column shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>
    /// Gets the arrow that shows the results are sorted by the column, <c>↑</c> or <c>↓</c>, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string SortGlyph { get; internal set; } = string.Empty;
}
