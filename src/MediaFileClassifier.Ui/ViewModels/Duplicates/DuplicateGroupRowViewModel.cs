using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// One row of the duplicate group list (E12-S03-T01): the type, the number of files and the wasted space.
/// </summary>
public sealed class DuplicateGroupRowViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateGroupRowViewModel"/> class.
    /// </summary>
    /// <param name="summary">The group.</param>
    /// <param name="localizer">UI strings.</param>
    public DuplicateGroupRowViewModel(DuplicateGroupSummary summary, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(localizer);

        Summary = summary;
        TypeText = localizer.Get(summary.Type == DuplicateGroupType.Exact ? "Duplicates_Type_Exact" : "Duplicates_Type_Similar");
        FilesText = localizer.Format("Duplicates_Files", summary.MemberCount);
        WastedText = localizer.Format("Duplicates_Wasted", Format.FileSize(summary.WastedBytes));
        AccessibleName = $"{TypeText}, {FilesText}, {WastedText}";
    }

    /// <summary>
    /// Gets the group.
    /// </summary>
    public DuplicateGroupSummary Summary { get; }

    /// <summary>
    /// Gets the type badge text, e.g. <c>Exact</c>.
    /// </summary>
    public string TypeText { get; }

    /// <summary>
    /// Gets the member count text, e.g. <c>3 files</c>.
    /// </summary>
    public string FilesText { get; }

    /// <summary>
    /// Gets the wasted space text, e.g. <c>82 MB wasted</c>.
    /// </summary>
    public string WastedText { get; }

    /// <summary>
    /// Gets the row's name for screen readers.
    /// </summary>
    public string AccessibleName { get; }
}
