using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
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
public sealed class LibraryCommands(LibraryDialogs dialogs, LibraryOpener opener, Localizer localizer, ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: create a library.</summary>
    public const string NewLibrary = "library.new";

    /// <summary>Command ID: open a library.</summary>
    public const string OpenLibrary = "library.open";

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
        var folders = registry.Add(new AppCommand(ManageFolders, CommandGroups.Library, 120, "Command_ManageFolders", dialogs.ShowIndexedFoldersAsync)
        {
            IconKey = "Icon.FolderOpen",
            IsVisible = LibraryDialogs.IsSupported,
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(folders.Refresh);
        }
    }
}
