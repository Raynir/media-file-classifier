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
/// The rescan commands (E02-S04): Remove missing items. Rescan comes here too.
/// </summary>
/// <param name="sender">Sends the use cases.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, which the commands need, or <c>null</c> where there's none.</param>
public sealed class RescanCommands(ISender sender, Localizer localizer, ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: delete the items whose files are gone, after a confirmation (E02-S04-T04).</summary>
    public const string RemoveMissing = "library.remove-missing";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        var removeMissing = registry.Add(new AppCommand(RemoveMissing, CommandGroups.Library, 130, "Command_RemoveMissing", RemoveMissingAsync)
        {
            IconKey = "Icon.Delete",
            IsVisible = IsSupported,
            WhyDisabled = NoLibrary,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(removeMissing.Refresh);
        }
    }

    private static bool IsSupported => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    private string? NoLibrary() => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null;

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
