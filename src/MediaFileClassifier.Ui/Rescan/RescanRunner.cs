using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;

namespace MediaFileClassifier.Ui.Rescan;

/// <summary>
/// Starts a rescan and tells the user how it went (E02-S04-T06). The summary is a message window when the job
/// finishes; E15's notifications replace it.
/// </summary>
/// <param name="sender">Sends the use cases.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, or <c>null</c> where there's none.</param>
public sealed class RescanRunner(ISender sender, Localizer localizer, ILibrarySession? session = null)
{
    /// <summary>
    /// How often a running rescan is asked whether it has finished.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Raised on the UI thread with the summary when a rescan started here has finished, before it's shown.
    /// </summary>
    public event EventHandler<RescanSummary>? Finished;

    /// <summary>
    /// Gets or sets a value indicating whether the summary is shown in a window when a rescan finishes. Tests turn it off.
    /// </summary>
    public bool ShowSummary { get; set; } = true;

    /// <summary>
    /// Starts a rescan of a folder or of the whole library, and follows it until it finishes.
    /// </summary>
    /// <param name="folderId">The folder, or <c>null</c> for the library.</param>
    /// <returns>What happened, and a sentence for the user when the rescan didn't start.</returns>
    public async Task<(StartRescanOutcome Outcome, string? Message)> StartAsync(Id<IndexedFolder>? folderId = null)
    {
        var result = await sender.Send(new StartRescanCommand(folderId));
        switch (result)
        {
            case { Outcome: StartRescanOutcome.Started, JobId: { } jobId }:
                _ = FollowAsync(jobId, session?.Current);
                return (result.Outcome, null);
            case { Outcome: StartRescanOutcome.Busy }:
                return (result.Outcome, localizer.Get("Rescan_Busy"));
            case { Outcome: StartRescanOutcome.NothingToScan }:
                return (result.Outcome, localizer.Get("Rescan_NoFolders"));
            default:
                return (result.Outcome, localizer.Get("Rescan_Unavailable"));
        }
    }

    /// <summary>
    /// Shows a message with one button, modal to the main window.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the message is closed.</returns>
    public async Task ShowMessageAsync(string message)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            await new LibraryMessageWindow(message, localizer.Get("Folders_Close"), null).ShowDialog<bool>(owner);
        }
    }

    // Stops quietly if the library closes meanwhile; the job carries on or is resumed with the library.
    private async Task FollowAsync(Id<MediaFileClassifier.Domain.Jobs.ProcessingJob> jobId, IOpenLibrary? library)
    {
        try
        {
            while (session is null || ReferenceEquals(session.Current, library))
            {
                await Task.Delay(PollInterval);
                if (await sender.Send(new GetRescanSummaryQuery(jobId)) is { } summary)
                {
                    Finished?.Invoke(this, summary);
                    if (ShowSummary)
                    {
                        await ShowMessageAsync(RescanSummaryText.Describe(localizer, summary));
                    }

                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The library closed under the poll; there's no summary to show.
        }
    }
}
