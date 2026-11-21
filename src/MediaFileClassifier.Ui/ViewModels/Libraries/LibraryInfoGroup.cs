using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// A titled group of rows of the library information view, e.g. "Items".
/// </summary>
/// <param name="headerKey">The string resource key of the header.</param>
/// <param name="rows">The rows.</param>
public sealed partial class LibraryInfoGroup(string headerKey, IReadOnlyList<LibraryInfoRow> rows) : ObservableObject
{
    /// <summary>
    /// Gets the string resource key of the header.
    /// </summary>
    public string HeaderKey { get; } = headerKey;

    /// <summary>
    /// Gets or sets the header, already localized.
    /// </summary>
    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    /// <summary>
    /// Gets the rows.
    /// </summary>
    public IReadOnlyList<LibraryInfoRow> Rows { get; } = rows;
}
