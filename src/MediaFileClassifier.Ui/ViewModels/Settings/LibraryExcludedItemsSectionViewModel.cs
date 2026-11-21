using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The excluded items in the Settings "Library" page: the same view as the dialog, without its title and Close button.
/// </summary>
public sealed class LibraryExcludedItemsSectionViewModel : LibraryEmbeddedSectionViewModel
{
    private readonly ExcludedItemsViewModel items;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryExcludedItemsSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="items">The excluded items view model.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryExcludedItemsSectionViewModel(ILibrarySession session, ExcludedItemsViewModel items, Action<Action>? post = null)
        : base(session, items, 300, post)
    {
        this.items = items;
        items.IsEmbedded = true;
    }

    /// <inheritdoc />
    protected override Task LoadAsync() => items.LoadAsync();
}
