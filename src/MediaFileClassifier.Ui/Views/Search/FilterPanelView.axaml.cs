using Avalonia.Controls;
using Avalonia.Data.Converters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Views.Search;

/// <summary>
/// The filter controls in the Filters flyout (E10-S02-T01).
/// </summary>
public partial class FilterPanelView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterPanelView"/> class.
    /// </summary>
    public FilterPanelView() => InitializeComponent();

    /// <summary>
    /// Gets a converter from an included tag to the accessible name of its remove button.
    /// </summary>
    public static IValueConverter RemoveIncludedName { get; } = new FuncValueConverter<string?, string>(tag => Localizer.Current.Format("Filter_TagRemoveIncluded", tag));

    /// <summary>
    /// Gets a converter from an excluded tag to the accessible name of its remove button.
    /// </summary>
    public static IValueConverter RemoveExcludedName { get; } = new FuncValueConverter<string?, string>(tag => Localizer.Current.Format("Filter_TagRemoveExcluded", tag));
}
