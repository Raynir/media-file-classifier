using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Ui.Activity;

/// <summary>
/// Takes the main window to the items an activity event is about (E15-S04-T02).
/// </summary>
public interface IActivityNavigator
{
    /// <summary>
    /// Shows a folder's items in the media browser and brings the main window to the front.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    void ShowFolder(Id<IndexedFolder> folderId);
}
