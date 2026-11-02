using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Libraries;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The welcome screen (E02-S01-T02), shown in the browser area while no library is open.
/// </summary>
/// <param name="dialogs">Opens the library dialogs.</param>
public sealed partial class WelcomeViewModel(LibraryDialogs dialogs) : ViewModelBase
{
    [RelayCommand]
    private Task CreateLibraryAsync() => dialogs.ShowCreateLibraryAsync();
}
