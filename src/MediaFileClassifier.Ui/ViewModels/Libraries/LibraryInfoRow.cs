using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// One label and value of the library information view (E02-S05-T02), e.g. "Images" and "10,000".
/// </summary>
public sealed partial class LibraryInfoRow : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInfoRow"/> class.
    /// </summary>
    /// <param name="labelKey">The string resource key of the label.</param>
    public LibraryInfoRow(string labelKey) => LabelKey = labelKey;

    /// <summary>
    /// Gets the string resource key of the label.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Gets or sets the label, already localized.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value, already formatted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets the name read by screen readers, e.g. "Images: 10,000".
    /// </summary>
    public string AccessibleName => $"{Label}: {Value}";
}
