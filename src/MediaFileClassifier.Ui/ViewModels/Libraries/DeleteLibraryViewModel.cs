using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.DeleteLibrary;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The delete-library confirmation (E02-S07-T02): it shows the item count and says that media files aren't touched, and
/// Delete is enabled only once the library's name is typed exactly, so one click can't delete a library.
/// </summary>
/// <param name="sender">Sends the use cases.</param>
/// <param name="localizer">UI strings.</param>
public sealed partial class DeleteLibraryViewModel(ISender sender, Localizer localizer) : ViewModelBase
{
    private string? directory;
    private bool loadFailed;

    /// <summary>
    /// Raised when the dialog should close, after deleting or on Cancel.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets the library's name, which the user types to confirm.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>Gets the heading, e.g. "Delete the library Family?".</summary>
    [ObservableProperty]
    public partial string Heading { get; private set; } = string.Empty;

    /// <summary>Gets what deleting removes, with the item count.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether it's the open library, which deleting closes first.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the prompt above the name box.</summary>
    [ObservableProperty]
    public partial string Prompt { get; private set; } = string.Empty;

    /// <summary>Gets or sets what the user typed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial string? TypedName { get; set; }

    /// <summary>Gets why the library can't be deleted, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial string? Error { get; private set; }

    /// <summary>Gets a value indicating whether the library is being deleted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets a value indicating whether the name can be typed: the library was read, can be deleted, and isn't
    /// being deleted.</summary>
    public bool CanConfirm => !IsBusy && !loadFailed && directory is not null && Name.Length > 0;

    /// <summary>Gets what deleting did, or <c>null</c> if the library wasn't deleted.</summary>
    public DeleteLibraryResult? Result { get; private set; }

    /// <summary>Gets the library folder.</summary>
    public string? LibraryDirectory => directory;

    /// <summary>
    /// Reads the library: its name and item count, or why it can't be deleted.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="fallbackName">The name to show when the library can't be read, e.g. from the recent list.</param>
    /// <returns>A task that completes when it's read.</returns>
    public async Task LoadAsync(string libraryDirectory, string fallbackName)
    {
        directory = libraryDirectory;
        var info = await sender.Send(new GetLibraryDeletionInfoQuery(libraryDirectory));
        Name = info.Name ?? fallbackName;
        IsOpen = info.IsOpen;
        Heading = localizer.Format("DeleteLibrary_Heading", Name);
        Summary = info.IsPartial
            ? localizer.Get("DeleteLibrary_Partial")
            : localizer.Format(info.ItemCount == 1 ? "DeleteLibrary_Summary_One" : "DeleteLibrary_Summary_Other", Format.Number(info.ItemCount));
        Prompt = localizer.Format("DeleteLibrary_Prompt", Name);
        loadFailed = info.Error is not null;
        Error = info.Error is { } error ? localizer.Get("LibraryOpenError_" + error) : null;
        OnPropertyChanged(nameof(CanConfirm));
    }

    // A failed delete can be tried again: typing clears its message. Why the library can't be deleted at all stays.
    partial void OnTypedNameChanged(string? value)
    {
        if (!loadFailed)
        {
            Error = null;
        }
    }

    private bool CanDelete() => CanConfirm && Error is null && string.Equals(TypedName?.Trim(), Name, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        IsBusy = true;
        try
        {
            var result = await sender.Send(new DeleteLibraryCommand(directory!));
            if (result.Deleted)
            {
                Result = result;

                // Not busy any more, or the window would refuse to close.
                IsBusy = false;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            var reason = localizer.Get("LibraryOpenError_" + result.Error);

            // Deleting closed the open library first; it stays closed (confirmed by the user), so say so.
            Error = IsOpen ? localizer.Format("DeleteLibrary_ClosedNotDeleted", reason) : reason;
            IsOpen = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Not while deleting: closing then would hide what the delete did.
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private bool IsIdle() => !IsBusy;
}
