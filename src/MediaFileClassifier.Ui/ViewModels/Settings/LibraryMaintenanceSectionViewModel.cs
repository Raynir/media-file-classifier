using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Maintenance;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The maintenance actions in the Settings "Library" page (E02-S06): the same commands as the Library menu, as buttons.
/// Each is disabled with its reason (such as "Open a library first") by the command itself.
/// </summary>
public sealed class LibraryMaintenanceSectionViewModel : LibrarySectionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMaintenanceSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="registry">The registered commands.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryMaintenanceSectionViewModel(ILibrarySession session, CommandRegistry registry, Action<Action>? post = null)
        : base(session, post)
    {
        ArgumentNullException.ThrowIfNull(registry);

        RefreshIndex = registry[MaintenanceCommands.RefreshIndex];
        RegenerateThumbnails = registry[MaintenanceCommands.RegenerateThumbnails];
        CheckIntegrity = registry[MaintenanceCommands.CheckIntegrity];
    }

    /// <summary>
    /// Gets "Refresh search index".
    /// </summary>
    public AppCommand RefreshIndex { get; }

    /// <summary>
    /// Gets "Regenerate thumbnails…".
    /// </summary>
    public AppCommand RegenerateThumbnails { get; }

    /// <summary>
    /// Gets "Check library integrity".
    /// </summary>
    public AppCommand CheckIntegrity { get; }

    /// <inheritdoc />
    protected override Task LoadAsync() => Task.CompletedTask;
}
