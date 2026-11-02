using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The library commands (E02).
/// </summary>
/// <param name="dialogs">Opens the library dialogs.</param>
public sealed class LibraryCommands(LibraryDialogs dialogs) : ICommandContributor
{
    /// <summary>Command ID: create a library.</summary>
    public const string NewLibrary = "library.new";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(NewLibrary, CommandGroups.Library, 100, "Command_NewLibrary", dialogs.ShowCreateLibraryAsync)
        {
            IconKey = "Icon.Library",
            IsVisible = LibraryDialogs.IsSupported,
        });
    }
}
