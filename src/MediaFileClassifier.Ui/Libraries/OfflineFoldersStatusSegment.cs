using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Libraries.CountOfflineFolders;
using MediatR;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The "1 folder offline" notice at the right end of the status bar (E13-S04-T02), shown while an indexed folder of
/// the open library can't be reached. It's recounted when a library opens, whenever the jobs start or stop, because
/// discovery notices a folder going offline, and when an availability check changed a folder's status (E14-S07-T04). Clicking it will open folder management once that
/// exists (E02-S03-T06).
/// </summary>
/// <param name="sender">Counts the offline folders.</param>
/// <param name="session">Holds the open library.</param>
/// <param name="feed">The throttled job progress.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="folderStatus">Tells when a folder went Offline or came back, or <see langword="null"/>.</param>
public sealed class OfflineFoldersStatusSegment(ISender sender, ILibrarySession session, ProgressFeed feed, Localizer localizer, FolderStatusChanges? folderStatus = null) : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "libraries.offline";

    private StatusBarSegment? segment;
    private int offline;
    private int activeJobs;
    private int version;

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, 1) { IconKey = "Icon.Status.Offline", IsVisible = false });
        session.CurrentChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        feed.Updated += (_, summary) =>
        {
            if (summary.ActiveJobCount != activeJobs)
            {
                activeJobs = summary.ActiveJobCount;
                _ = RefreshAsync();
            }
        };
        localizer.Changed += (_, _) => Show();
        if (folderStatus is not null)
        {
            folderStatus.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var current = ++version;
        int count;
        try
        {
            count = await sender.Send(new CountOfflineFoldersQuery());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The library closed under us, or can't be read: the notice isn't worth failing for.
            count = 0;
        }

        // A newer refresh already asked, so this answer may be stale.
        if (current == version)
        {
            offline = count;
            Show();
        }
    }

    private void Show()
    {
        if (segment is null)
        {
            return;
        }

        segment.IsVisible = offline > 0;
        segment.Text = localizer.Format(offline == 1 ? "StatusBar_FoldersOffline_One" : "StatusBar_FoldersOffline_Other", Format.Number(offline));
        segment.ToolTip = localizer.Get("StatusBar_FoldersOffline_ToolTip");
    }
}
