using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.UseCases.Models.GetModels;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Models;

/// <summary>
/// The model management section of the AI &amp; Models settings (E17-S08-T01): every tier of every catalogue model,
/// installed or not, and installed models the catalogue doesn't know. Each row can be downloaded, imported, verified or
/// deleted. The list follows the model store.
/// </summary>
public sealed partial class ModelManagementViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly IModelDialogs dialogs;
    private readonly Localizer localizer;
    private readonly IModelAvailability? availability;
    private readonly IModelStore? store;
    private readonly IFolderLauncher? folderLauncher;
    private readonly ILogger logger;
    private int refreshes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelManagementViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="dialogs">The consent and file dialogs.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="availability">Tells when models are installed or deleted, or <see langword="null"/> where the app runs without AI.</param>
    /// <param name="store">The model store, whose folder the section can open, or <see langword="null"/> where the app runs without AI.</param>
    /// <param name="folderLauncher">Opens folders in the file manager, or <see langword="null"/> if the platform can't.</param>
    /// <param name="logger">Logger.</param>
    public ModelManagementViewModel(
        ISender sender,
        IModelDialogs dialogs,
        Localizer localizer,
        IModelAvailability? availability = null,
        IModelStore? store = null,
        IFolderLauncher? folderLauncher = null,
        ILogger<ModelManagementViewModel>? logger = null)
    {
        this.sender = sender;
        this.dialogs = dialogs;
        this.localizer = localizer;
        this.availability = availability;
        this.store = store;
        this.folderLauncher = folderLauncher;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        localizer.Changed += OnLocalizerChanged;
        if (availability is not null)
        {
            availability.Changed += OnModelsChanged;
            Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        }
    }

    /// <summary>
    /// Gets the models, one row per tier.
    /// </summary>
    public ObservableCollection<ModelRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets the error of the last refresh, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    public partial string LoadError { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the list couldn't be read.
    /// </summary>
    public bool HasLoadError => LoadError.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the model folder can be opened in the file manager.
    /// </summary>
    public bool CanOpenFolder => store is not null && folderLauncher is not null;

    /// <summary>
    /// Reads the list again. Rows that are downloading, verifying or deleting keep their state.
    /// </summary>
    /// <returns>A task that completes when the list is up to date.</returns>
    public async Task RefreshAsync()
    {
        var refresh = ++refreshes;
        IReadOnlyList<ModelListEntry> entries;
        try
        {
            entries = await sender.Send(new GetModelsQuery());
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            LogRefreshFailed(ex);
            LoadError = localizer.Get("Models_LoadFailed");
            return;
        }

        if (refresh != refreshes)
        {
            // A later refresh started meanwhile; its result is newer.
            return;
        }

        LoadError = string.Empty;
        var keep = new List<ModelRowViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            var row = Rows.FirstOrDefault(row => row.Matches(entry));
            if (row is null)
            {
                row = new ModelRowViewModel(entry, sender, dialogs, localizer, logger);
            }
            else
            {
                row.Update(entry);
            }

            keep.Add(row);
        }

        // Rebuild in the query's order without replacing the row objects, so focus and state survive.
        for (var index = 0; index < keep.Count; index++)
        {
            var current = Rows.IndexOf(keep[index]);
            if (current < 0)
            {
                Rows.Insert(index, keep[index]);
            }
            else if (current != index)
            {
                Rows.Move(current, index);
            }
        }

        while (Rows.Count > keep.Count)
        {
            Rows[^1].Dispose();
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (availability is not null)
        {
            availability.Changed -= OnModelsChanged;
        }

        localizer.Changed -= OnLocalizerChanged;
        foreach (var row in Rows)
        {
            row.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private async Task OpenFolderAsync()
    {
        if (store is null || folderLauncher is null)
        {
            return;
        }

        try
        {
            await folderLauncher.OpenFolderAsync(store.RootDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOpenFolderFailed(ex);
        }
    }

    private void OnModelsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());

    private void OnLocalizerChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows)
        {
            row.RefreshTexts();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening the models folder failed")]
    private partial void LogOpenFolderFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing the models failed")]
    private partial void LogRefreshFailed(Exception exception);
}
