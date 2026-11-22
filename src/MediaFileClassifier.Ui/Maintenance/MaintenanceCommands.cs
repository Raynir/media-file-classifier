using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;
using MediaFileClassifier.UseCases.Maintenance.CompactDatabase;
using MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;
using MediaFileClassifier.UseCases.Maintenance.RestoreBackup;
using MediaFileClassifier.UseCases.Search.RebuildSearchIndex;
using MediatR;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// The library maintenance commands (E02-S06-T01): Refresh search index, Regenerate thumbnails and Check library
/// integrity, in their own band of the Library menu. They're commands, so the library settings (MFC-133) can show them
/// too. None of them changes metadata or embeddings, or starts an analysis.
/// </summary>
/// <param name="sender">Sends the use cases.</param>
/// <param name="runner">Runs the index refresh and the integrity check in the background.</param>
/// <param name="dialogs">Shows the questions and reports.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, which the commands need, or <c>null</c> where there's none.</param>
public sealed class MaintenanceCommands(
    ISender sender,
    MaintenanceRunner runner,
    MaintenanceDialogs dialogs,
    Localizer localizer,
    ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: rebuild the keyword and vector indexes from the stored data.</summary>
    public const string RefreshIndex = "library.refresh-index";

    /// <summary>Command ID: make missing or outdated thumbnails again, or all of them.</summary>
    public const string RegenerateThumbnails = "library.regenerate-thumbnails";

    /// <summary>Command ID: run the database integrity check and show the report.</summary>
    public const string CheckIntegrity = "library.check-integrity";

    /// <summary>Command ID: compact the library database (VACUUM) to give free space back to the disk.</summary>
    public const string CompactDatabase = "library.compact-database";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        AppCommand[] commands =
        [
            registry.Add(new AppCommand(RefreshIndex, CommandGroups.Library, 200, "Command_RefreshIndex", RefreshIndexAsync)
            {
                IconKey = "Icon.Search",
                IsVisible = MaintenanceDialogs.IsSupported,
                WhyDisabled = NoLibrary,
            }),
            registry.Add(new AppCommand(RegenerateThumbnails, CommandGroups.Library, 210, "Command_RegenerateThumbnails", RegenerateThumbnailsAsync)
            {
                IconKey = "Icon.Image",
                IsVisible = MaintenanceDialogs.IsSupported,
                WhyDisabled = NoLibrary,
            }),
            registry.Add(new AppCommand(CheckIntegrity, CommandGroups.Library, 220, "Command_CheckIntegrity", CheckIntegrityAsync)
            {
                IconKey = "Icon.Info",
                IsVisible = MaintenanceDialogs.IsSupported,
                WhyDisabled = NoLibrary,
            }),
            registry.Add(new AppCommand(CompactDatabase, CommandGroups.Library, 230, "Command_CompactDatabase", CompactDatabaseAsync)
            {
                IsVisible = MaintenanceDialogs.IsSupported,
                WhyDisabled = NoLibrary,
            }),
        ];
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                foreach (var command in commands)
                {
                    command.Refresh();
                }
            });
        }
    }

    /// <summary>
    /// Rebuilds the search indexes in the background, and says so when it's done.
    /// </summary>
    /// <returns>How it ended.</returns>
    public async Task<MaintenanceRunState> RefreshIndexAsync()
    {
        var run = await runner.RunAsync(
            MaintenanceTask.RefreshIndex,
            async (progress, token) =>
            {
                await sender.Send(new RebuildSearchIndexCommand(progress), token);
                return true;
            });
        await ShowEndAsync(run.State, "Maintenance_IndexRefreshed");
        return run.State;
    }

    /// <summary>
    /// Asks which thumbnails to regenerate, then queues them: the missing or outdated ones, or all of them after the
    /// cache is deleted.
    /// </summary>
    /// <returns>What happened, or <c>null</c> when the user cancelled.</returns>
    public async Task<RegenerateThumbnailsOutcome?> RegenerateThumbnailsAsync()
    {
        if (await dialogs.ChooseThumbnailsAsync() is not { } all)
        {
            return null;
        }

        var result = await sender.Send(new RegenerateThumbnailsCommand(all));
        var message = result.Outcome switch
        {
            RegenerateThumbnailsOutcome.NothingToDo => localizer.Get("Maintenance_ThumbnailsComplete"),
            RegenerateThumbnailsOutcome.Busy => localizer.Get("Maintenance_ThumbnailsBusy"),
            RegenerateThumbnailsOutcome.ImportRunning => localizer.Get("Maintenance_ThumbnailsImportRunning"),
            RegenerateThumbnailsOutcome.Unavailable => localizer.Get("Command_NoLibraryOpen"),
            _ => null,
        };
        if (message is not null)
        {
            await dialogs.ShowAsync(message, localizer.Get("Folders_Close"));
        }

        return result.Outcome;
    }

    /// <summary>
    /// Runs the integrity check in the background and shows the report. When the database is damaged and there's a
    /// backup, offers to restore it, and says how that went.
    /// </summary>
    /// <returns>The report, or <c>null</c> when the check didn't finish.</returns>
    public async Task<IntegrityReport?> CheckIntegrityAsync()
    {
        // A restore closes the library for a moment, before the disabled state reaches the menu.
        if (session is { Current: null })
        {
            await dialogs.ShowAsync(localizer.Get("Command_NoLibraryOpen"), localizer.Get("Folders_Close"));
            return null;
        }

        var run = await runner.RunAsync(MaintenanceTask.CheckIntegrity, (_, token) => sender.Send(new CheckLibraryIntegrityQuery(), token));
        if (run is { State: MaintenanceRunState.Finished, Result: { } report })
        {
            var text = IntegrityReportText.Describe(localizer, report);
            if (report is { IsOk: false, Backup: { } backup })
            {
                if (await dialogs.AskAsync(text, localizer.Get("Integrity_RestoreBackup"), localizer.Get("Integrity_NotNow")))
                {
                    // Not on the runner: closing the library would cancel it half-way.
                    var restored = await sender.Send(new RestoreIntegrityBackupCommand(backup));
                    await dialogs.ShowAsync(IntegrityReportText.DescribeRestore(localizer, backup, restored), localizer.Get("Folders_Close"));
                }
            }
            else
            {
                await dialogs.ShowAsync(text, localizer.Get("Folders_Close"));
            }

            return report;
        }

        await ShowEndAsync(run.State, null);
        return null;
    }

    /// <summary>
    /// Says how much space compacting would give back and asks, then compacts the database in the background (MFC-1022).
    /// It refuses while jobs have work left, and tells the user why.
    /// </summary>
    /// <returns>What happened, or <c>null</c> when the user declined or no library is open.</returns>
    public async Task<CompactDatabaseResult?> CompactDatabaseAsync()
    {
        if (await sender.Send(new GetDatabaseSpaceQuery()) is not { } space)
        {
            await dialogs.ShowAsync(localizer.Get("Command_NoLibraryOpen"), localizer.Get("Folders_Close"));
            return null;
        }

        if (!DatabaseCompaction.IsWorthwhile(space))
        {
            await dialogs.ShowAsync(localizer.Get("Maintenance_NothingToReclaim"), localizer.Get("Folders_Close"));
            return new CompactDatabaseResult(CompactDatabaseOutcome.NothingToReclaim, space);
        }

        var question = localizer.Format("Maintenance_CompactAsk", Format.FileSize(space.FreeBytes));
        if (!await dialogs.AskAsync(question, localizer.Get("Maintenance_Compact"), localizer.Get("Integrity_NotNow")))
        {
            return null;
        }

        var run = await runner.RunAsync(
            MaintenanceTask.CompactDatabase,
            (_, token) => sender.Send(new CompactDatabaseCommand(), token));
        if (run is not { State: MaintenanceRunState.Finished, Result: { } result })
        {
            await ShowEndAsync(run.State, null);
            return null;
        }

        var message = result.Outcome switch
        {
            CompactDatabaseOutcome.Compacted => localizer.Format(
                "Maintenance_Compacted",
                Format.FileSize(result.Before?.FileBytes ?? 0),
                Format.FileSize(result.After?.FileBytes ?? 0)),
            CompactDatabaseOutcome.JobsRunning => localizer.Get("Maintenance_CompactJobsRunning"),
            CompactDatabaseOutcome.NothingToReclaim => localizer.Get("Maintenance_NothingToReclaim"),
            CompactDatabaseOutcome.NotEnoughDiskSpace => localizer.Format(
                "Maintenance_CompactNoSpace",
                Format.FileSize(result.Before is { } before ? DatabaseCompaction.RequiredDiskBytes(before) : 0)),
            _ => localizer.Get("Command_NoLibraryOpen"),
        };
        await dialogs.ShowAsync(message, localizer.Get("Folders_Close"));
        return result;
    }

    private string? NoLibrary() => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null;

    private async Task ShowEndAsync(MaintenanceRunState state, string? finishedKey)
    {
        var message = state switch
        {
            MaintenanceRunState.Busy => localizer.Get("Maintenance_Busy"),
            MaintenanceRunState.Finished when finishedKey is not null => localizer.Get(finishedKey),
            _ => null,
        };
        if (message is not null)
        {
            await dialogs.ShowAsync(message, localizer.Get("Folders_Close"));
        }
    }
}
