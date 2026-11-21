using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.RecentLibraryList;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// A library in the welcome screen's recent list (E02-S02-T02).
/// </summary>
public sealed partial class RecentLibraryViewModel : ViewModelBase
{
    private readonly Func<RecentLibraryViewModel, Task> open;
    private readonly Func<RecentLibraryViewModel, Task> remove;
    private readonly Func<RecentLibraryViewModel, Task>? delete;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecentLibraryViewModel"/> class.
    /// </summary>
    /// <param name="item">The list entry.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="open">Opens the library.</param>
    /// <param name="remove">Removes it from the list.</param>
    /// <param name="delete">Deletes the library after a confirmation (E02-S07), or <c>null</c> where that isn't offered.</param>
    public RecentLibraryViewModel(
        RecentLibraryItem item,
        Localizer localizer,
        Func<RecentLibraryViewModel, Task> open,
        Func<RecentLibraryViewModel, Task> remove,
        Func<RecentLibraryViewModel, Task>? delete = null)
    {
        this.open = open;
        this.remove = remove;
        this.delete = delete;
        Name = item.Library.Name;
        Path = item.Library.Path;
        IsAvailable = item.IsAvailable;
        Details = IsAvailable
            ? localizer.Format("Welcome_LastOpened", Format.Date(item.Library.LastOpenedAt.ToLocalTime()))
            : localizer.Get("Welcome_Unavailable");
        RemoveLabel = localizer.Format("Welcome_RemoveRecent", Name);
        DeleteLabel = localizer.Format("Welcome_DeleteLibrary", Name);
    }

    /// <summary>Gets the library name.</summary>
    public string Name { get; }

    /// <summary>Gets the library folder.</summary>
    public string Path { get; }

    /// <summary>Gets a value indicating whether the library can be opened now.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets when it was last opened, or that it's unavailable.</summary>
    public string Details { get; }

    /// <summary>Gets the accessible name of the remove button.</summary>
    public string RemoveLabel { get; }

    /// <summary>Gets the accessible name of the delete button.</summary>
    public string DeleteLabel { get; }

    /// <summary>Gets a value indicating whether the library can be deleted from the list: it's reachable and deleting is offered.</summary>
    public bool CanDelete => IsAvailable && delete is not null;

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task OpenAsync() => open(this);

    [RelayCommand]
    private Task RemoveAsync() => remove(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => delete!(this);
}
