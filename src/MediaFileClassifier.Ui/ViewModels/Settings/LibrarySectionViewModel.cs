using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A section of the Settings "Library" page (E17-S04). It follows the open library, and its content loads while the
/// section is on screen (the sections are singletons that live as long as the app): the view calls <see cref="AttachAsync"/> when it's shown and <see cref="Detach"/> when it's hidden.
/// </summary>
public abstract partial class LibrarySectionViewModel : ViewModelBase
{
    private readonly ILibrarySession session;
    private readonly Action<Action> post;
    private int attached;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibrarySectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    protected LibrarySectionViewModel(ILibrarySession session, Action<Action>? post = null)
    {
        this.session = session;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        HasLibrary = session.Current is not null;
        session.CurrentChanged += OnCurrentChanged;
    }

    /// <summary>
    /// Gets how often the content is refreshed while the section is shown, or <see langword="null"/> for never.
    /// </summary>
    public virtual TimeSpan? RefreshInterval => null;

    /// <summary>
    /// Gets a value indicating whether a library is open.
    /// </summary>
    [ObservableProperty]
    public partial bool HasLibrary { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the section is on screen.
    /// </summary>
    protected bool IsShown => attached > 0;

    /// <summary>
    /// Called when the section is shown; loads its content.
    /// </summary>
    /// <returns>A task that completes when the content is loaded.</returns>
    public async Task AttachAsync()
    {
        if (attached++ > 0)
        {
            return;
        }

        OnShown();
        if (HasLibrary)
        {
            await LoadAsync();
        }
    }

    /// <summary>
    /// Called when the section is hidden.
    /// </summary>
    public void Detach()
    {
        if (attached == 0 || --attached > 0)
        {
            return;
        }

        OnHidden();
    }

    /// <summary>
    /// Refreshes the content, if the section is shown and a library is open. The view calls it every <see cref="RefreshInterval"/>.
    /// </summary>
    /// <returns>A task that completes when the content is up to date.</returns>
    public async Task RefreshAsync()
    {
        if (IsShown && HasLibrary)
        {
            await RefreshCoreAsync();
        }
    }

    /// <summary>
    /// Loads the content when the section is shown or another library opens.
    /// </summary>
    /// <returns>A task that completes when the content is loaded.</returns>
    protected abstract Task LoadAsync();

    /// <summary>
    /// Refreshes the content periodically; by default loads it again.
    /// </summary>
    /// <returns>A task that completes when the content is up to date.</returns>
    protected virtual Task RefreshCoreAsync() => LoadAsync();

    /// <summary>
    /// Called when the section is shown, before it loads.
    /// </summary>
    protected virtual void OnShown()
    {
    }

    /// <summary>
    /// Called when the section is hidden.
    /// </summary>
    protected virtual void OnHidden()
    {
    }

    /// <summary>
    /// Called on the UI thread after the open library changed.
    /// </summary>
    protected virtual void OnLibraryChanged()
    {
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => post(() =>
    {
        HasLibrary = session.Current is not null;
        OnLibraryChanged();
        if (IsShown && HasLibrary)
        {
            _ = LoadSafelyAsync();
        }
    });

    private async Task LoadSafelyAsync()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The library closed again meanwhile; the next change or refresh loads it.
        }
    }
}
