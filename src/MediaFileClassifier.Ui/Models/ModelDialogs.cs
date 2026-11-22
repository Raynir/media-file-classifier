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
    public async Task<IReadOnlyList<string>?> PickModelFilesAsync()
    {
        if (FindOwner() is not { } owner)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localizer.Get("ModelImport_Title"),
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return null;
        }

        return FindModelFiles(folder);
    }

    /// <summary>
    /// Lists the model files directly inside a folder: the app matches them to the catalogue by name.
    /// </summary>
    /// <param name="folder">The folder the user picked.</param>
    /// <returns>The files' paths, which are none when the folder has no model files or can't be read.</returns>
    internal static IReadOnlyList<string> FindModelFiles(string folder)
    {
        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder)
                    .Where(path => Path.GetExtension(path) is { } extension
                        && (extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".model", StringComparison.OrdinalIgnoreCase))),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Window? FindOwner() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow
            : null;
}
