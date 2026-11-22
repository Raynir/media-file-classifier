using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.About;

/// <summary>
/// The About overlay (MFC-1023): the product name, the version with its commit, and the repository.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// The repository, where the releases and the licence are.
    /// </summary>
    public const string RepositoryUrl = "https://github.com/Raynir/media-file-classifier";

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class.
    /// </summary>
    /// <param name="state">Whether the overlay is open.</param>
    /// <param name="version">The running build's version.</param>
    public AboutViewModel(AboutState state, AppVersion version)
    {
        State = state;
        Version = version;
    }

    /// <summary>
    /// Gets whether the overlay is open.
    /// </summary>
    public AboutState State { get; }

    /// <summary>
    /// Gets the running build's version.
    /// </summary>
    public AppVersion Version { get; }

    /// <summary>
    /// Gets the version text, e.g. <c>1.0.0 (abc1234)</c>.
    /// </summary>
    public string VersionText => Version.Text;

    /// <summary>
    /// Gets the repository address.
    /// </summary>
    public string Repository => RepositoryUrl;

    [RelayCommand]
    private void Close() => State.IsOpen = false;
}
