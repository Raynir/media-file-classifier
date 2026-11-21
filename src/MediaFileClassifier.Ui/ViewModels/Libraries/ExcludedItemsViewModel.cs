using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Exclusions.ForgetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.GetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.RestoreExcludedItems;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The Excluded items view (E02-S10-T04): the items the user removed from the library, which can be restored or
/// forgotten. Forget asks first, inline, because it deletes the items' user values. It doesn't depend on the window
/// around it, so the settings window can host it later (MFC-133).
/// </summary>
public sealed partial class ExcludedItemsViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly Localizer localizer;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExcludedItemsViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the use cases.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logs failures.</param>
    public ExcludedItemsViewModel(ISender sender, Localizer localizer, ILogger<ExcludedItemsViewModel>? logger = null)
    {
        this.sender = sender;
        this.localizer = localizer;
        this.logger = logger ?? NullLogger<ExcludedItemsViewModel>.Instance;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the view is hosted in the Settings "Library" page, which has its own
    /// heading and no Close button.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmbedded { get; set; }

    /// <summary>
    /// Raised when the user closes the view.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the removed items, newest removal first.
    /// </summary>
    public ObservableCollection<ExcludedItemRowViewModel> Items { get; } = [];

    /// <summary>
    /// Gets or sets the selected items. The view sets it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ForgetWarning))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(ForgetCommand))]
    public partial IReadOnlyList<ExcludedItemRowViewModel> SelectedItems { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether any item is selected.
    /// </summary>
    public bool HasSelection => SelectedItems.Count > 0;

    /// <summary>
    /// Gets a value indicating whether no item is removed.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Gets the header: how many items are removed, and that their files are still on disk.
    /// </summary>
    public string Header => Items.Count == 1
        ? localizer["ExcludedItems_HeaderOne"]
        : localizer.Format("ExcludedItems_Header", Format.Number(Items.Count));

    /// <summary>
    /// Gets a value indicating whether Forget waits for the user to confirm.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(ForgetCommand))]
    public partial bool IsConfirmingForget { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Restore or Forget is running, so another can't start on the same items.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(ForgetCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Gets what Forget deletes, for the selection.
    /// </summary>
    public string ForgetWarning => SelectedItems.Count == 1
        ? localizer["ExcludedItems_ForgetWarningOne"]
        : localizer.Format("ExcludedItems_ForgetWarning", Format.Number(SelectedItems.Count));

    /// <summary>
    /// Gets the error of the last action, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the last action failed.
    /// </summary>
    public bool HasError => Error.Length > 0;

    /// <summary>
    /// Lists the removed items again.
    /// </summary>
    /// <returns>A task that completes when the list is up to date.</returns>
    public async Task LoadAsync()
    {
        var items = await sender.Send(new GetExcludedItemsQuery());
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(new ExcludedItemRowViewModel(item.Id, item.FileName, item.FolderPath, Format.Date(item.RemovedAt)));
        }

        SelectedItems = [];
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Header));
    }

    private bool CanChange() => HasSelection && !IsConfirmingForget && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task RestoreAsync() => RunAsync(async ids => await sender.Send(new RestoreExcludedItemsCommand(ids)));

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Forget() => IsConfirmingForget = true;

    [RelayCommand]
    private async Task ConfirmForgetAsync()
    {
        IsConfirmingForget = false;
        await RunAsync(async ids => await sender.Send(new ForgetExcludedItemsCommand(ids)));
    }

    [RelayCommand]
    private void CancelForget() => IsConfirmingForget = false;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task RunAsync(Func<IReadOnlyCollection<Id<MediaItem>>, Task> action)
    {
        Error = string.Empty;
        IsBusy = true;
        try
        {
            await action([.. SelectedItems.Select(row => row.Id)]);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogActionFailed(logger, ex);
            Error = localizer["ExcludedItems_Failed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't restore or forget the excluded items")]
    private static partial void LogActionFailed(ILogger logger, Exception exception);
}

/// <summary>
/// One item the user removed from the library.
/// </summary>
/// <param name="Id">The item.</param>
/// <param name="FileName">The file name.</param>
/// <param name="FolderPath">The folder the file is in.</param>
/// <param name="RemovedOn">When it was removed, formatted.</param>
public sealed record ExcludedItemRowViewModel(Id<MediaItem> Id, string FileName, string FolderPath, string RemovedOn);
