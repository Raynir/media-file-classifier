using MediaFileClassifier.UseCases.Models.GetModelOffer;

namespace MediaFileClassifier.Ui.Models;

/// <summary>
/// The dialogs of getting a model (E06-S12-T04).
/// </summary>
public interface IModelDialogs
{
    /// <summary>
    /// Asks the user to agree to a download, showing its size, license and source (D032).
    /// </summary>
    /// <param name="offer">What would be downloaded.</param>
    /// <returns><see langword="true"/> if the user agreed.</returns>
    Task<bool> ConfirmDownloadAsync(ModelOffer offer);

    /// <summary>
    /// Asks the user for the folder that holds a model's files, and returns the model files in it. The use case matches them
    /// to the catalogue by name, and names the files that are missing.
    /// </summary>
    /// <returns>The model files' paths (an empty list when the folder has none), or <see langword="null"/> if the user cancelled.</returns>
    Task<IReadOnlyList<string>?> PickModelFilesAsync();
}
