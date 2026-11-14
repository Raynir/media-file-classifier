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
    /// Asks the user for a model's files, to import them.
    /// </summary>
    /// <returns>The picked files' paths, or an empty list if the user cancelled.</returns>
    Task<IReadOnlyList<string>> PickModelFilesAsync();
}
