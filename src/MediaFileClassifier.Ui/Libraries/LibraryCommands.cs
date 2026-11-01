using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The library commands (E02).
/// </summary>
/// <param name="dialogs">Opens the library dialogs.</param>
/// <param name="opener">Opens libraries.</param>
public sealed class LibraryCommands(LibraryDialogs dialogs, LibraryOpener opener) : ICommandContributor
{
    /// <summary>Command ID: create a library.</summary>
    public const string NewLibrary = "library.new";

    /// <summary>Command ID: open a library.</summary>
    public const string OpenLibrary = "library.open";

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
    }
}
