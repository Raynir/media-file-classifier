using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The library information in the Settings "Library" page: where the library is, with Reveal and Copy path, above the
/// counts and disk usage of <see cref="LibraryInfoViewModel"/>.
/// </summary>
public sealed partial class LibraryInformationSectionViewModel : LibrarySectionViewModel
{
    private readonly ILibrarySession session;
    private readonly IPlatformServices platform;
    private readonly ITextClipboard clipboard;
    private readonly ILogger<LibraryInformationSectionViewModel> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInformationSectionViewModel"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="info">The statistics and disk usage.</param>
    /// <param name="platform">Shows the folder in the file manager.</param>
    /// <param name="clipboard">Copies the path.</param>
    /// <param name="logger">Logs failures.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryInformationSectionViewModel(
        ILibrarySession session,
        LibraryInfoViewModel info,
        IPlatformServices platform,
        ITextClipboard clipboard,
        ILogger<LibraryInformationSectionViewModel>? logger = null,
        Action<Action>? post = null)
        : base(session, post)
    {
        this.session = session;
        this.platform = platform;
        this.clipboard = clipboard;
        this.logger = logger ?? NullLogger<LibraryInformationSectionViewModel>.Instance;
        Info = info;
        Location = LocationOf(session);
    }

    /// <summary>
    /// Gets the counts and disk usage.
    /// </summary>
    public LibraryInfoViewModel Info { get; }

    /// <summary>
    /// Gets the folder the open library is stored in, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    public partial string Location { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the platform has a file manager to show the folder in.
    /// </summary>
    public bool CanReveal => platform.CanRevealInFileExplorer;

    /// <inheritdoc />
    protected override Task LoadAsync() => Task.CompletedTask;

    /// <inheritdoc />
    protected override void OnShown()
    {
        Info.Attach();
        Location = LocationOf(session);
    }

    /// <inheritdoc />
    protected override void OnHidden() => Info.Detach();

    /// <inheritdoc />
    protected override void OnLibraryChanged() => Location = LocationOf(session);

    private static string LocationOf(ILibrarySession session) => session.Current?.Layout.RootDirectory ?? string.Empty;

    private bool HasLocation() => Location.Length > 0;

    private bool CanRevealLocation() => HasLocation() && CanReveal;

    [RelayCommand(CanExecute = nameof(CanRevealLocation))]
    private async Task RevealAsync()
    {
        var library = session.Current;
        if (library is null)
        {
            return;
        }

        try
        {
            await platform.RevealInFileExplorerAsync(new FileLocation(new FolderHandle(library.Layout.RootDirectory, library.Library.Name), string.Empty));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "Couldn't show the library folder {Path}", library.Layout.RootDirectory);
        }
    }

    [RelayCommand(CanExecute = nameof(HasLocation))]
    private async Task CopyPathAsync()
    {
        try
        {
            await clipboard.SetTextAsync(Location);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Another program may hold the clipboard; the user can try again.
            logger.LogWarning(exception, "Couldn't copy the library path");
        }
    }
}
