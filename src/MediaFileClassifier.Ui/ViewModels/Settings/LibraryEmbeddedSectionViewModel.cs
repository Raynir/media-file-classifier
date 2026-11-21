using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A library section that shows an existing view inside a box of fixed height, such as the indexed folders.
/// </summary>
public abstract class LibraryEmbeddedSectionViewModel : LibrarySectionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryEmbeddedSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="content">The view model of the hosted view.</param>
    /// <param name="contentHeight">The box's height.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    protected LibraryEmbeddedSectionViewModel(ILibrarySession session, ViewModelBase content, double contentHeight, Action<Action>? post = null)
        : base(session, post)
    {
        Content = content;
        ContentHeight = contentHeight;
    }

    /// <summary>
    /// Gets the hosted view's view model.
    /// </summary>
    public ViewModelBase Content { get; }

    /// <summary>
    /// Gets the height of the box the view is in.
    /// </summary>
    public double ContentHeight { get; }
}
