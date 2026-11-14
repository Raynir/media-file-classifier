namespace MediaFileClassifier.Ui.Analysis;

/// <summary>
/// The dialogs of the analysis controls (E06-S06): a confirmation before a large run, and a notice.
/// </summary>
public interface IAnalysisDialogs
{
    /// <summary>
    /// Asks the user to confirm.
    /// </summary>
    /// <param name="message">The question.</param>
    /// <param name="confirmLabel">The label of the button that confirms.</param>
    /// <param name="cancelLabel">The label of the button that declines.</param>
    /// <returns><see langword="true"/> if the user confirmed.</returns>
    Task<bool> ConfirmAsync(string message, string confirmLabel, string cancelLabel);

    /// <summary>
    /// Tells the user something.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the user closes the notice.</returns>
    Task ShowAsync(string message);
}
