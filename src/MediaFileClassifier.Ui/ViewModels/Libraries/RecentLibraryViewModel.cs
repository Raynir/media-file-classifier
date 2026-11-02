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

    /// <summary>
    /// Initializes a new instance of the <see cref="RecentLibraryViewModel"/> class.
    /// </summary>
    /// <param name="item">The list entry.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="open">Opens the library.</param>
    /// <param name="remove">Removes it from the list.</param>
    public RecentLibraryViewModel(RecentLibraryItem item, Localizer localizer, Func<RecentLibraryViewModel, Task> open, Func<RecentLibraryViewModel, Task> remove)
    {
        this.open = open;
        this.remove = remove;
        Name = item.Library.Name;
        Path = item.Library.Path;
        IsAvailable = item.IsAvailable;
        Details = IsAvailable
            ? localizer.Format("Welcome_LastOpened", Format.Date(item.Library.LastOpenedAt.ToLocalTime()))
            : localizer.Get("Welcome_Unavailable");
        RemoveLabel = localizer.Format("Welcome_RemoveRecent", Name);
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

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task OpenAsync() => open(this);

    [RelayCommand]
    private Task RemoveAsync() => remove(this);
}
