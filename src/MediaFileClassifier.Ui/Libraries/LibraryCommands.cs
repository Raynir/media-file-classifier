using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The library commands (E02).
/// </summary>
/// <param name="dialogs">Opens the library dialogs.</param>
/// <param name="opener">Opens libraries.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library, which the folder commands need, or <c>null</c> where there's none.</param>
/// <param name="platform">The folder picker for Add folder, or <c>null</c> where there's none.</param>
/// <param name="importer">Imports picked files, or <c>null</c> where it isn't registered.</param>
public sealed class LibraryCommands(
    LibraryDialogs dialogs,
    LibraryOpener opener,
    Localizer localizer,
    ILibrarySession? session = null,
    IPlatformServices? platform = null,
    FileImporter? importer = null) : ICommandContributor
{
    /// <summary>Command ID: create a library.</summary>
    public const string NewLibrary = "library.new";

    /// <summary>Command ID: open a library.</summary>
    public const string OpenLibrary = "library.open";

    /// <summary>Command ID: pick a folder and add it to the open library (E02-S03, E13-S03).</summary>
    public const string AddFolder = "library.add-folder";

    /// <summary>Command ID: pick files and import them into the open library (E03-S05).</summary>
    public const string ImportFiles = "library.import-files";

    /// <summary>Command ID: manage the open library's indexed folders.</summary>
    public const string ManageFolders = "library.folders";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(NewLibrary, CommandGroups.Library, 100, "Command_NewLibrary", dialogs.ShowCreateLibraryAsync)
        {
            IconKey = "Icon.Library",
            IsVisible = LibraryDialogs.IsSupported,
        });
        registry.Add(new AppCommand(OpenLibrary, CommandGroups.Library, 110, "Command_OpenLibrary", opener.PickAndOpenAsync)
        {
            IconKey = "Icon.FolderOpen",
            IsVisible = LibraryDialogs.IsSupported,
        });
        var addFolder = registry.Add(new AppCommand(AddFolder, CommandGroups.Library, 115, "Command_AddFolder", AddFolderAsync)
        {
            IconKey = "Icon.FolderAdd",
            IsVisible = LibraryDialogs.IsSupported && platform is not null,
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        var importFiles = registry.Add(new AppCommand(ImportFiles, CommandGroups.Library, 116, "Command_ImportFiles", () => importer?.PickAndImportAsync() ?? Task.CompletedTask)
        {
            IconKey = "Icon.Image",
            IsVisible = LibraryDialogs.IsSupported && importer?.CanPick == true,
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        var folders = registry.Add(new AppCommand(ManageFolders, CommandGroups.Library, 120, "Command_ManageFolders", () => dialogs.ShowIndexedFoldersAsync())
        {
            IconKey = "Icon.FolderOpen",
            IsVisible = LibraryDialogs.IsSupported,
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                addFolder.Refresh();
                importFiles.Refresh();
                folders.Refresh();
            });
        }
    }

    /// <summary>
    /// Asks for a folder and opens the indexed folders dialog with it added, as dropping a folder on the
    /// navigation panel does, so the overlap rules and confirmations apply (E02-S03).
    /// </summary>
    private async Task AddFolderAsync()
    {
        if (platform is not null && await platform.PickFolderAsync(localizer.Get("Folders_PickFolder")) is { } folder)
        {
            await dialogs.ShowIndexedFoldersAsync([folder]);
        }
    }
}
