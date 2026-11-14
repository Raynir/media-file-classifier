using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Models.GetModelOffer;

namespace MediaFileClassifier.Ui.ViewModels.Models;

/// <summary>
/// The download consent (D032): the model's name, size, license and source, and the user's agreement.
/// </summary>
/// <param name="offer">What would be downloaded.</param>
/// <param name="localizer">UI strings.</param>
public sealed partial class ModelDownloadConsentViewModel(ModelOffer offer, Localizer localizer) : ViewModelBase
{
    /// <summary>
    /// Raised when the dialog should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the model's name.
    /// </summary>
    public string ModelName => offer.DisplayName;

    /// <summary>
    /// Gets the download size.
    /// </summary>
    public string Size => Format.FileSize(offer.Size);

    /// <summary>
    /// Gets the license name.
    /// </summary>
    public string License => offer.License;

    /// <summary>
    /// Gets the license text's address.
    /// </summary>
    public Uri LicenseUrl => offer.LicenseUrl;

    /// <summary>
    /// Gets the publisher's page.
    /// </summary>
    public Uri Source => offer.Source;

    /// <summary>
    /// Gets the download button's text, with the size.
    /// </summary>
    public string DownloadLabel => localizer.Format("ModelConsent_Download", Size);

    /// <summary>
    /// Gets a value indicating whether the user agreed to the download.
    /// </summary>
    public bool Agreed { get; private set; }

    [RelayCommand]
    private void Download()
    {
        Agreed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
