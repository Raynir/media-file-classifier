using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediaFileClassifier.UseCases.Libraries.ValidateLibraryLocation;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The create-library dialog (E02-S01-T03): first a name and a location, checked as the user types; then, once the
/// library is created and open, the folders to index.
/// </summary>
public sealed partial class CreateLibraryViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly IPlatformServices platform;
    private readonly Localizer localizer;
    private bool nameEdited;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateLibraryViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the library commands.</param>
    /// <param name="platform">Folder pickers.</param>
    /// <param name="locations">The default location.</param>
    /// <param name="localizer">UI strings.</param>
    public CreateLibraryViewModel(ISender sender, IPlatformServices platform, ILibraryLocations locations, Localizer localizer)
    {
        this.sender = sender;
        this.platform = platform;
        this.localizer = localizer;
        Location = locations.DefaultParentDirectory;
    }

    /// <summary>
    /// Raised when the dialog should close: after Done, or on Cancel before the library is created.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder the library goes in.
    /// </summary>
    [ObservableProperty]
    public partial string Location { get; set; }

    /// <summary>
    /// Gets why the library can't be created yet, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string? Error { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the name and location are acceptable.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsValid { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the library has been created, so the dialog shows the folders step.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsStep))]
    public partial bool IsFoldersStep { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the dialog shows the name and location step.
    /// </summary>
    public bool IsDetailsStep => !IsFoldersStep;

    /// <summary>
    /// Gets why the last picked folder wasn't added, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial string? FolderMessage { get; private set; }

    /// <summary>
    /// Gets the folders added to the new library.
    /// </summary>
    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>
    /// Checks the name and location again. The dialog calls it when it opens; edits call it too.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task ValidateAsync()
    {
        var error = await sender.Send(new ValidateLibraryLocationQuery(Name, Location));
        IsValid = error is null;

        // Don't greet the user with "enter a name" before they've typed one.
        Error = error is null || (error == LibraryLocationError.NameMissing && !nameEdited) ? null : LocationMessage(error.Value);
    }

    private string LocationMessage(LibraryLocationError error) => error == LibraryLocationError.NotEnoughSpace
        ? localizer.Format("LibraryLocationError_NotEnoughSpace", Format.FileSize(LibraryLocationRules.MinimumFreeBytes))
        : localizer.Get("LibraryLocationError_" + error);

    partial void OnNameChanged(string value)
    {
        nameEdited = true;
        _ = ValidateAsync();
    }

    partial void OnLocationChanged(string value) => _ = ValidateAsync();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (await platform.PickFolderAsync(localizer.Get("CreateLibrary_PickLocation")) is { } folder)
        {
            Location = folder.Id;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var result = await sender.Send(new CreateLibraryCommand(Name, Location));
        if (result.Succeeded)
        {
            IsFoldersStep = true;
            Error = null;
        }
        else
        {
            IsValid = false;
            Error = result.LocationError is { } locationError
                ? LocationMessage(locationError)
                : localizer.Get("LibraryOpenError_" + result.OpenError);
        }
    }

    private bool CanCreate() => IsValid && !CreateCommand.IsRunning;

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (await platform.PickFolderAsync(localizer.Get("CreateLibrary_PickFolder")) is { } folder)
        {
            // Overlaps are handled as in the folders dialog, without asking: the library is new and empty (E02-S03-T02).
            var result = await sender.Send(new AddLibraryFoldersCommand([folder]));
            foreach (var merged in result.Merged)
            {
                Folders.Remove(merged.DisplayPath);
            }

            foreach (var added in result.Added)
            {
                Folders.Add(added.DisplayPath);
            }

            FolderMessage = result.Refused.Count == 0 ? null : localizer.Format("Folders_InsideIndexed", folder.Id, result.Refused[0].CoveredBy.DisplayPath);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
