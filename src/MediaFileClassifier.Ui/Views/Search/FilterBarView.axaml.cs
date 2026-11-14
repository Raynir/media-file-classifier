using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace MediaFileClassifier.Ui.Views.Search;

/// <summary>
/// The quick filter chips and the Filters button above the results (E10-S02).
/// </summary>
public partial class FilterBarView : UserControl
{
    /// <summary>
    /// Gets a converter from a count to whether it is above zero.
    /// </summary>
    public static IValueConverter IsPositive { get; } = new FuncValueConverter<int, bool>(count => count > 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterBarView"/> class.
    /// </summary>
    public FilterBarView() => InitializeComponent();
}
