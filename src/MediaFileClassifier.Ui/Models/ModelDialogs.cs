using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Models;
using MediaFileClassifier.Ui.Views.Models;
using MediaFileClassifier.UseCases.Models.GetModelOffer;

namespace MediaFileClassifier.Ui.Models;

/// <summary>
/// Shows the model dialogs on the desktop, modal to the active window: the main window, or the settings window (E17-S08).
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class ModelDialogs(Localizer localizer) : IModelDialogs
{
    /// <inheritdoc />
    public async Task<bool> ConfirmDownloadAsync(ModelOffer offer)
    {
        if (FindOwner() is not { } owner)
        {
            return false;
        }

        var viewModel = new ModelDownloadConsentViewModel(offer, localizer);
        var window = new ModelDownloadConsentWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(owner);
        return viewModel.Agreed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickModelFilesAsync()
    {
        if (FindOwner() is not { } owner)
        {
            return [];
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizer.Get("ModelImport_Title"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(localizer.Get("ModelImport_FileType")) { Patterns = ["*.onnx", "*.model"] }],
        });
        return [.. files.Select(file => file.TryGetLocalPath()).OfType<string>()];
    }

    private static Window? FindOwner() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow
            : null;
}
