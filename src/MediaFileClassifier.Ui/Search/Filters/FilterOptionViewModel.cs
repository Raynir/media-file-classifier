using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// One check box of a multi-select filter (E10-S02-T01).
/// </summary>
public sealed partial class FilterOptionViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterOptionViewModel"/> class.
    /// </summary>
    /// <param name="value">The value stored in the filter.</param>
    /// <param name="text">The text shown.</param>
    public FilterOptionViewModel(string value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>
    /// Gets the value stored in the filter.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the text shown.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the option is ticked.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
