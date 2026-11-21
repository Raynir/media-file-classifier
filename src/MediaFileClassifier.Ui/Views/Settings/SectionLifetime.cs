using MediaFileClassifier.Ui.ViewModels.Settings;

namespace MediaFileClassifier.Ui.Views.Settings;

/// <summary>
/// Runs a library section's load and refresh for a view. A failure only means the library closed or a folder was
/// unavailable meanwhile, so the next refresh tries again.
/// </summary>
internal static class SectionLifetime
{
    /// <summary>
    /// Tells the section it's shown.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns>A task that completes when the section has loaded.</returns>
    public static async Task AttachAsync(LibrarySectionViewModel section)
    {
        try
        {
            await section.AttachAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The next refresh loads it.
        }
    }

    /// <summary>
    /// Refreshes the section.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns>A task that completes when the section is up to date.</returns>
    public static async Task RefreshAsync(LibrarySectionViewModel section)
    {
        try
        {
            await section.RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The next tick tries again.
        }
    }
}
