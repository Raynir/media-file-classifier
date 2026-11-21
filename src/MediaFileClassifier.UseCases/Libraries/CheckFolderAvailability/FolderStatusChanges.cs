using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Reports that an indexed folder of the open library went Offline or came back Online (E14-S07-T04), so what shows its
/// items' availability refreshes: the results, the details panel and the offline folders notice. A folder's status isn't
/// part of its items' rows, so <c>IMediaItemRepository.Changed</c> doesn't cover it. It follows the session and passes on
/// <see cref="IFolderRepository.StatusChanged"/> of the open library, so every writer of a folder's status is covered:
/// availability checks, discovery, rescans and imports.
/// </summary>
public sealed class FolderStatusChanges
{
    private readonly ILibrarySession? session;
    private readonly Lock syncRoot = new();
    private IFolderRepository? watched;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderStatusChanges"/> class.
    /// </summary>
    /// <param name="session">Holds the open library whose folders are watched, or <see langword="null"/> to rely on
    /// <see cref="Raise"/> alone.</param>
    public FolderStatusChanges(ILibrarySession? session = null)
    {
        this.session = session;
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Watch();
            Watch();
        }
    }

    /// <summary>
    /// Raised after a folder of the open library changed status. It's raised on the writing thread, once per stored
    /// change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raises <see cref="Changed"/>.
    /// </summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void Watch()
    {
        lock (syncRoot)
        {
            var folders = session?.Current?.Folders;
            if (ReferenceEquals(folders, watched))
            {
                return;
            }

            if (watched is not null)
            {
                watched.StatusChanged -= OnStatusChanged;
            }

            watched = folders;
            if (watched is not null)
            {
                watched.StatusChanged += OnStatusChanged;
            }
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Raise();
}
