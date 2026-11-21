using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.RecentLibraryList;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The welcome screen (E02-S01-T02, E02-S02-T02), shown in the browser area while no library is open: create,
/// open, the recent libraries, and why the last library didn't reopen.
/// </summary>
/// <param name="dialogs">Opens the library dialogs.</param>
/// <param name="opener">Opens libraries.</param>
/// <param name="sender">Sends the recent-list requests.</param>
/// <param name="localizer">UI strings.</param>
public sealed partial class WelcomeViewModel(LibraryDialogs dialogs, LibraryOpener opener, ISender sender, Localizer localizer) : ViewModelBase
{
    /// <summary>
    /// Gets the recent libraries, most recent first.
    /// </summary>
    public ObservableCollection<RecentLibraryViewModel> Recent { get; } = [];

    /// <summary>
    /// Gets or sets why a library couldn't be opened, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>
    /// Reloads the recent list, rechecking which libraries are reachable.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    public async Task RefreshAsync()
    {
        var items = await sender.Send(new GetRecentLibrariesQuery());
        Recent.Clear();
        foreach (var item in items)
        {
            Recent.Add(new RecentLibraryViewModel(item, localizer, OpenRecentAsync, RemoveRecentAsync, LibraryDialogs.IsSupported ? DeleteRecentAsync : null));
        }
    }

    [RelayCommand]
    private Task CreateLibraryAsync() => dialogs.ShowCreateLibraryAsync();

    [RelayCommand]
    private Task OpenLibraryAsync() => opener.PickAndOpenAsync();

    private Task OpenRecentAsync(RecentLibraryViewModel library) => opener.OpenAsync(library.Path);

    private async Task DeleteRecentAsync(RecentLibraryViewModel library)
    {
        if (await dialogs.ShowDeleteLibraryAsync(library.Path, library.Name, isOpen: false))
        {
            await RefreshAsync();
        }
    }

    private async Task RemoveRecentAsync(RecentLibraryViewModel library)
    {
        await sender.Send(new RemoveRecentLibraryCommand(library.Path));
        Recent.Remove(library);
    }
}
