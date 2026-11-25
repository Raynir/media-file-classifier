using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The indexed folders in the Settings "Library" page: the same view as the dialog, without its title and Close button.
/// </summary>
public sealed class LibraryFoldersSectionViewModel : LibraryEmbeddedSectionViewModel
{
    private readonly IndexedFoldersViewModel folders;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryFoldersSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="folders">The folders view model.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryFoldersSectionViewModel(ILibrarySession session, IndexedFoldersViewModel folders, Action<Action>? post = null)
        : base(session, folders, 560, post)
    {
        this.folders = folders;
        folders.IsEmbedded = true;
    }

    /// <summary>
    /// Gets how often the folder list is refreshed, so states and counts follow an import: as in the dialog.
    /// </summary>
    public static TimeSpan FoldersRefreshInterval { get; } = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public override TimeSpan? RefreshInterval => FoldersRefreshInterval;

    /// <inheritdoc />
    protected override Task LoadAsync() => folders.OpenAsync();

    /// <inheritdoc />
    protected override Task RefreshCoreAsync() => folders.RefreshAsync();
}
