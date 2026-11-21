using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The list view's columns (E14-S02), in display order. The header and every row lay out their cells from it and
/// update when <see cref="LayoutChanged"/> fires. Use it on the UI thread.
/// </summary>
public sealed partial class ListColumnsViewModel : ViewModelBase
{
    /// <summary>
    /// The default setup: every column but the folder shows (MFC-453).
    /// </summary>
    private static readonly (ListColumnKind Kind, double Width, bool IsVisible)[] Defaults =
    [
        (ListColumnKind.Thumbnail, 48, true),
        (ListColumnKind.Name, 280, true),
        (ListColumnKind.Type, 72, true),
        (ListColumnKind.DateTaken, 150, true),
        (ListColumnKind.Size, 88, true),
        (ListColumnKind.Resolution, 110, true),
        (ListColumnKind.Duration, 80, true),
        (ListColumnKind.Status, 120, true),
        (ListColumnKind.Folder, 260, false),
        (ListColumnKind.Favorite, 40, true),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="ListColumnsViewModel"/> class with the default setup.
    /// </summary>
    public ListColumnsViewModel()
    {
        Apply(DefaultSetup());
    }

    /// <summary>
    /// Raised when a column's width, visibility or position changed, so the header and rows lay out again.
    /// </summary>
    public event EventHandler? LayoutChanged;

    /// <summary>
    /// Gets the columns in display order, hidden ones included.
    /// </summary>
    public ObservableCollection<ListColumnViewModel> Columns { get; } = [];

    /// <summary>
    /// Gets the width of the visible columns together.
    /// </summary>
    public double TotalWidth => Columns.Where(column => column.IsVisible).Sum(column => column.Width);

    /// <summary>
    /// Gets a column.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <returns>The column.</returns>
    public ListColumnViewModel this[ListColumnKind kind] => Columns.First(column => column.Kind == kind);

    private static List<ListColumnViewModel> DefaultSetup() =>
        [.. Defaults.Select(entry => new ListColumnViewModel(entry.Kind, entry.Width, entry.IsVisible))];

    private void Apply(IReadOnlyList<ListColumnViewModel> columns)
    {
        foreach (var column in Columns)
        {
            column.PropertyChanged -= OnColumnChanged;
        }

        Columns.Clear();
        foreach (var column in columns)
        {
            column.PropertyChanged += OnColumnChanged;
            Columns.Add(column);
        }

        RaiseLayoutChanged();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListColumnViewModel.Width) or nameof(ListColumnViewModel.IsVisible))
        {
            RaiseLayoutChanged();
        }
    }

    private void RaiseLayoutChanged()
    {
        OnPropertyChanged(nameof(TotalWidth));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
