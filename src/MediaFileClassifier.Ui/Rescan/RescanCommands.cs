using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.UseCases.Rescan.RemoveMissingItems;
using MediatR;

namespace MediaFileClassifier.Ui.Rescan;

/// <summary>
/// The rescan commands (E02-S04): Rescan library, with the toolbar button, and Remove missing items. Rescan folder is a button of the
/// indexed folders dialog.
/// </summary>
/// <param name="sender">Sends the use cases.</param>
/// <param name="runner">Starts rescans and shows their summaries.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, which the commands need, or <c>null</c> where there's none.</param>
public sealed class RescanCommands(ISender sender, RescanRunner runner, Localizer localizer, ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: rescan every folder of the open library (E02-S04-T06).</summary>
    public const string Rescan = "library.rescan";

    /// <summary>Command ID: delete the items whose files are gone, after a confirmation (E02-S04-T04).</summary>
    public const string RemoveMissing = "library.remove-missing";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        var rescan = registry.Add(new AppCommand(Rescan, CommandGroups.Library, 125, "Command_Rescan", RescanAsync)
        {
            IconKey = "Icon.Rescan",
            IsVisible = IsSupported,
            WhyDisabled = NoLibrary,
        });
        var removeMissing = registry.Add(new AppCommand(RemoveMissing, CommandGroups.Library, 130, "Command_RemoveMissing", RemoveMissingAsync)
        {
            IconKey = "Icon.Delete",
            IsVisible = IsSupported,
            WhyDisabled = NoLibrary,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                rescan.Refresh();
                removeMissing.Refresh();
            });
        }
    }

    private static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    private string? NoLibrary() => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null;

    private async Task RescanAsync()
    {
        if ((await runner.StartAsync()).Message is { } message)
        {
            await runner.ShowMessageAsync(message);
        }
    }

    private async Task RemoveMissingAsync()
    {
        var count = await sender.Send(new CountMissingItemsQuery());
        var items = IndexedFolderViewModel.ItemCountText(localizer, count);
        if (count == 0)
        {
            await ShowAsync(localizer.Get("Command_NoMissingItems"), localizer.Get("Folders_Close"), null);
            return;
        }

        if (!await ShowAsync(localizer.Format("Rescan_RemoveMissingConfirm", items), localizer.Get("Rescan_RemoveMissingButton"), localizer.Get("Rescan_Keep")))
        {
            return;
        }

        var removed = await sender.Send(new RemoveMissingItemsCommand());
        await ShowAsync(localizer.Format("Rescan_MissingRemoved", IndexedFolderViewModel.ItemCountText(localizer, removed)), localizer.Get("Folders_Close"), null);
    }

    private static async Task<bool> ShowAsync(string message, string primary, string? secondary)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return false;
        }

        return await new LibraryMessageWindow(message, primary, secondary).ShowDialog<bool>(owner);
    }
}
