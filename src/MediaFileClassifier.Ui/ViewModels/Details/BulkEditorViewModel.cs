using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
using MediaFileClassifier.UseCases.Tags;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The metadata editor in bulk mode (E07-S03): shown in the details panel while several items are selected. It shows
/// what the items share, "Multiple values" where they differ, and each tag with how many items have it. Like the
/// single-item editor it works on a staged copy: nothing changes until the user saves (D019), and a field the user
/// doesn't touch is left alone.
/// </summary>
public sealed partial class BulkEditorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Gets the number of items above which a save asks the user to confirm.</summary>
    internal const int ConfirmThreshold = 20;

    /// <summary>Gets the number of items above which a save shows progress and can be stopped.</summary>
    internal const int LargeSaveThreshold = 500;

    private readonly ISender sender;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private readonly BulkUndoStore? undoStore;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private CancellationTokenSource? saving;
    private bool undoing;
    private BulkMetadataView? shown;
    private int loads;
    private CancellationTokenSource? loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkEditorViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the use cases.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logs a load or save that failed.</param>
    /// <param name="undoStore">Holds what the last bulk edit changed, for Undo.</param>
    public BulkEditorViewModel(ISender sender, Localizer localizer, ILogger? logger = null, BulkUndoStore? undoStore = null)
    {
        this.sender = sender;
        this.undoStore = undoStore;
        if (undoStore is not null)
        {
            undoStore.Changed += OnUndoChanged;
            CanUndo = undoStore.Count > 0;
        }

        this.localizer = localizer;
        this.logger = logger ?? NullLogger.Instance;
        Fields =
        [
            Create(DescriptiveField.Title, "Details_Title_Label"),
            Create(DescriptiveField.Description, "Details_Description"),
            Create(DescriptiveField.Summary, "Editor_Field_Summary"),
            Create(DescriptiveField.Notes, "Editor_Field_Notes"),
        ];
        localizer.Changed += (_, _) => RefreshLabels();
    }

    /// <summary>
    /// Gets the fields a bulk edit can set: title, description, summary and notes.
    /// </summary>
    public IReadOnlyList<BulkFieldViewModel> Fields { get; }

    /// <summary>
    /// Gets the tags any selected item shows, most shared first, and the tags typed in.
    /// </summary>
    public ObservableCollection<BulkTagViewModel> Tags { get; } = [];

    /// <summary>
    /// Gets the selected items the editor works on.
    /// </summary>
    public IReadOnlyList<Id<MediaItem>> ItemIds { get; private set; } = [];

    /// <summary>
    /// Gets the number of selected items.
    /// </summary>
    [ObservableProperty]
    public partial int ItemCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the aggregate is being read.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the selection shows any tag.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTags { get; private set; }

    /// <summary>
    /// Gets a value indicating whether anything is staged and not saved.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>
    /// Gets or sets a notice about the staged changes, shown in the Save bar, or <see langword="null"/>: the selection is
    /// held after it moved away (E07-S12-T01), or its items changed in the background (E07-S12-T02).
    /// </summary>
    [ObservableProperty]
    public partial string? Notice { get; internal set; }

    /// <summary>
    /// Gets or sets the text typed into the tag input.
    /// </summary>
    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a message about a tag that was refused, shown under the tag input, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? TagError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a save is running.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSaving { get; private set; }

    /// <summary>
    /// Gets the number of items a running save has handled.
    /// </summary>
    [ObservableProperty]
    public partial int SaveProgress { get; private set; }

    /// <summary>
    /// Gets the text of a running save, e.g. "Saving 300 of 2000 items", or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? SaveProgressText { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the save is big enough to show progress and a way to stop it.
    /// </summary>
    public bool IsLargeSave => ItemCount > LargeSaveThreshold;

    /// <summary>
    /// Gets a value indicating whether the user is asked to confirm a save that changes many items.
    /// </summary>
    [ObservableProperty]
    public partial bool IsConfirming { get; private set; }

    /// <summary>
    /// Gets the question of the inline confirmation, e.g. "300 items will change.".
    /// </summary>
    public string ConfirmText => localizer.Format("Bulk_Confirm", ItemCount);

    /// <summary>
    /// Gets a message about a save that failed, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    /// <summary>
    /// Gets what the last save or undo did, shown with Undo, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? ResultMessage { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last bulk edit can be undone.
    /// </summary>
    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    /// <summary>
    /// Gets a task for the latest load, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Shows what the selection shares. Whatever was staged for the items shown before is dropped.
    /// </summary>
    /// <param name="itemIds">The selected items; none empties the editor.</param>
    /// <returns>A task that completes when the aggregate is shown.</returns>
    public Task LoadAsync(IReadOnlyList<Id<MediaItem>> itemIds) => Loading = LoadCoreAsync(itemIds);

    /// <summary>
    /// Asks to save: a save of more than <see cref="ConfirmThreshold"/> items first asks the user to confirm, in the
    /// Save bar; the next request saves.
    /// </summary>
    /// <returns>A task that completes when the request is handled, and the save, if it ran, finished.</returns>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public Task RequestSaveAsync()
    {
        if (ItemCount > ConfirmThreshold && !IsConfirming)
        {
            IsConfirming = true;
            OnPropertyChanged(nameof(ConfirmText));
            return Task.CompletedTask;
        }

        return SaveAsync();
    }

    /// <summary>
    /// Saves every staged change to every selected item (D019, D072). The items are written in batches; the progress
    /// shows and the save can be stopped, which keeps the items already written. A value the service refuses is shown
    /// next to its field, nothing is written, and the staged changes stay.
    /// </summary>
    /// <returns>A task that completes when the save has finished.</returns>
    public async Task SaveAsync()
    {
        var ids = ItemIds;
        var edits = BuildEdits();
        if (IsSaving || undoing || ids.Count == 0 || edits.Count == 0)
        {
            return;
        }

        IsSaving = true;
        IsConfirming = false;
        SaveError = null;
        ResultMessage = null;
        SaveProgress = 0;
        SaveProgressText = localizer.Format("Bulk_Saving", 0, ids.Count);
        RequestSaveCommand.NotifyCanExecuteChanged();
        saving = new CancellationTokenSource();
        var token = saving.Token;
        var progress = new Progress<int>(handled =>
        {
            SaveProgress = handled;
            SaveProgressText = localizer.Format("Bulk_Saving", handled, ids.Count);
        });
        try
        {
            var report = new CountingProgress(progress, ids.Count);
            var result = await sender.Send(new EditMetadataCommand(ids, edits, CaptureUndo: true, report), token);
            if (result.Error is { } error)
            {
                ShowError(error);
                return;
            }

            ResultMessage = localizer.Format(result.ChangedItemIds.Count == 0 ? "Bulk_Result_None" : "Bulk_Result_Saved", result.ChangedItemIds.Count);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = localizer.Format("Bulk_Result_Stopped", undoStore?.Count ?? 0, ids.Count);
        }
        catch (Exception ex)
        {
            LogSaveFailed(logger, ex);
            SaveError = localizer.Get("Editor_Error_SaveFailed");
            return;
        }
        finally
        {
            IsSaving = false;
            SaveProgressText = null;
            saving?.Dispose();
            saving = null;
            CanUndo = undoStore?.Count > 0;
            RequestSaveCommand.NotifyCanExecuteChanged();
        }

        // The saved values come back from the library, so the editor shows what is stored. Staged changes go: they
        // are saved or, after a stop, partly saved, and the user starts again from what is stored.
        await LoadAsync(ids);
    }

    /// <summary>
    /// Stops a running save after the batch it is writing. Items written already stay written, and can be undone.
    /// </summary>
    [RelayCommand]
    public void CancelSave() => saving?.Cancel();

    /// <summary>
    /// Puts back what the last bulk edit changed.
    /// </summary>
    /// <returns>A task that completes when the items are put back.</returns>
    [RelayCommand]
    public async Task UndoAsync()
    {
        if (IsSaving || undoing || undoStore is null || undoStore.Count == 0)
        {
            return;
        }

        undoing = true;
        try
        {
            var result = await sender.Send(new UndoBulkEditCommand());
            ResultMessage = result.RestoredItemIds.Count == 0 ? null : localizer.Format("Bulk_Result_Undone", result.RestoredItemIds.Count);
        }
        catch (Exception ex)
        {
            LogSaveFailed(logger, ex);
            SaveError = localizer.Get("Bulk_Error_UndoFailed");
        }
        finally
        {
            undoing = false;
        }

        CanUndo = undoStore.Count > 0;
        if (ItemIds.Count > 0 && !IsDirty)
        {
            await LoadAsync(ItemIds);
        }
    }

    /// <summary>
    /// Drops every staged change.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        if (IsSaving || undoing)
        {
            // The save works on the changes staged when it started; editing waits for it.
            return;
        }

        foreach (var field in Fields)
        {
            field.Discard();
        }

        ApplyTags(shown);
        NewTagText = string.Empty;
        TagError = null;
        SaveError = null;
        UpdateDirty();
    }

    /// <summary>
    /// Stages a tag the user typed: it is added to every selected item on save. A tag the items show already is staged
    /// for the items that lack it; a removal or rejection staged on it is taken back.
    /// </summary>
    /// <param name="text">The tag as typed.</param>
    /// <returns><see langword="true"/> if the text was taken, <see langword="false"/> if it is empty.</returns>
    public bool AddTag(string? text)
    {
        var clean = TagNormalizer.CleanUserTag(text);
        if (clean.Length == 0)
        {
            return false;
        }

        TagError = null;
        var existing = Tags.FirstOrDefault(chip => string.Equals(chip.Text, clean, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            AddChip(new BulkTagViewModel(clean, 0, ItemCount, 0, 0, TagStage.Added, localizer));
        }
        else if (existing.IsDropped)
        {
            existing.Stage = TagStage.Unchanged;
        }
        else if (existing.Stage == TagStage.Unchanged && existing.UserCount < ItemCount)
        {
            existing.Stage = TagStage.Added;
        }

        UpdateDirty();
        return true;
    }

    /// <summary>
    /// Takes the text in the tag input as a tag and clears the input.
    /// </summary>
    [RelayCommand]
    public void AddTypedTag()
    {
        if (AddTag(NewTagText))
        {
            NewTagText = string.Empty;
        }
    }

    /// <summary>
    /// Stages removing a tag from the items that have it as a user tag. A tag typed and not saved is dropped.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void RemoveTag(BulkTagViewModel? chip) => Stage(chip, TagStage.Removed, chip?.CanRemove == true);

    /// <summary>
    /// Stages rejecting a tag on the items that show it, so AI runs and the file don't add it again.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void RejectTag(BulkTagViewModel? chip) => Stage(chip, TagStage.Rejected, chip?.CanReject == true);

    /// <summary>
    /// Takes back the removal or rejection staged on a tag, or the add staged on one the items show already.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void UndoTag(BulkTagViewModel? chip) => Stage(chip, TagStage.Unchanged, chip is { IsStaged: true });

    /// <summary>
    /// Lists the edits the staged changes stand for: nothing for a field the user didn't touch.
    /// </summary>
    /// <returns>The edits, in the order the service applies them.</returns>
    internal List<MetadataEdit> BuildEdits()
    {
        var edits = new List<MetadataEdit>();
        edits.AddRange(Fields.Select(field => field.ToEdit()).OfType<MetadataEdit>());

        // Removals and rejections go first, so a tag staged for removal and typed again nets out in the service.
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Removed).Select(chip => (MetadataEdit)new MetadataEdit.RemoveTag(TagName.From(chip.Text))));
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Rejected).Select(chip => (MetadataEdit)new MetadataEdit.RejectTag(TagName.From(chip.Text))));
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Added).Select(chip => (MetadataEdit)new MetadataEdit.AddTag(chip.Text)));
        return edits;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        loading?.Cancel();
        loading?.Dispose();
        saving?.Cancel();
        saving?.Dispose();
        if (undoStore is not null)
        {
            undoStore.Changed -= OnUndoChanged;
        }
    }

    private void Stage(BulkTagViewModel? chip, TagStage stage, bool allowed)
    {
        if (chip is null || !allowed)
        {
            return;
        }

        if (chip.Stage == TagStage.Added && chip.ItemCount == 0)
        {
            // A tag typed in has nothing stored to remove, reject or go back to: it is dropped.
            Tags.Remove(chip);
            chip.PropertyChanged -= OnChipChanged;
        }
        else
        {
            chip.Stage = stage;
        }

        HasTags = Tags.Count > 0;
        UpdateDirty();
    }

    private BulkFieldViewModel Create(DescriptiveField field, string labelKey)
    {
        var item = new BulkFieldViewModel(field, labelKey, localizer);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BulkFieldViewModel.IsDirty))
            {
                UpdateDirty();
            }
        };
        return item;
    }

    private void RefreshLabels()
    {
        foreach (var field in Fields)
        {
            field.Label = localizer.Get(field.LabelKey);
        }
    }

    private void AddChip(BulkTagViewModel chip)
    {
        chip.PropertyChanged += OnChipChanged;
        Tags.Add(chip);
        HasTags = true;
    }

    private void OnChipChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkTagViewModel.Stage))
        {
            UpdateDirty();
        }
    }

    private bool CanSave() => IsDirty && !IsSaving;

    private void OnUndoChanged(object? sender, EventArgs e)
    {
        // Raised on the writer's thread after a save, or on the thread that opened another library.
        if (context is not null)
        {
            context.Post(_ => CanUndo = undoStore?.Count > 0, null);
        }
        else
        {
            CanUndo = undoStore?.Count > 0;
        }
    }

    private void ShowError(MetadataValidationError error)
    {
        var field = Fields.FirstOrDefault(candidate => candidate.Field == error.Field);
        var message = localizer.Format("Editor_Error_" + error.Code, error.Limit ?? 0, error.Tag ?? string.Empty);
        if (error.Code is MetadataValidationCode.TagEmpty or MetadataValidationCode.TagTooLong or MetadataValidationCode.TagInvalidCharacter or MetadataValidationCode.TooManyTags)
        {
            TagError = message;
        }
        else if (field is null)
        {
            SaveError = message;
        }
        else
        {
            field.Error = message;
        }
    }

    private void UpdateDirty()
    {
        IsDirty = Fields.Any(field => field.IsDirty) || Tags.Any(chip => chip.IsStaged);
        IsConfirming = false;
        RequestSaveCommand.NotifyCanExecuteChanged();
        if (!IsDirty)
        {
            // A notice is about staged changes.
            Notice = null;
        }
    }

    private async Task LoadCoreAsync(IReadOnlyList<Id<MediaItem>> itemIds)
    {
        var load = ++loads;
        loading?.Cancel();
        loading?.Dispose();
        loading = new CancellationTokenSource();
        var token = loading.Token;
        if (!itemIds.SequenceEqual(ItemIds))
        {
            ResultMessage = null;
            SaveError = null;
        }

        ItemIds = itemIds;
        ItemCount = itemIds.Count;
        OnPropertyChanged(nameof(IsLargeSave));
        IsLoading = itemIds.Count > 0;
        BulkMetadataView? view = null;
        if (itemIds.Count > 0)
        {
            try
            {
                view = await sender.Send(new GetBulkMetadataQuery(itemIds), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogLoadFailed(logger, ex);
            }
        }

        if (load != loads)
        {
            return;
        }

        IsLoading = false;
        Apply(view);
    }

    private void Apply(BulkMetadataView? view)
    {
        shown = view;
        ItemCount = view?.ItemCount ?? ItemCount;
        foreach (var field in Fields)
        {
            if (view is { Fields.Count: > 0 })
            {
                field.Load(view[field.Field], view.ItemCount);
            }
            else
            {
                field.Load(new BulkFieldView(field.Field, null, false, 0, 0, 0, 0), 0);
            }
        }

        ApplyTags(view);
        NewTagText = string.Empty;
        TagError = null;
        UpdateDirty();
    }

    private void ApplyTags(BulkMetadataView? view)
    {
        foreach (var chip in Tags)
        {
            chip.PropertyChanged -= OnChipChanged;
        }

        Tags.Clear();
        foreach (var tag in view?.Tags ?? [])
        {
            AddChip(BulkTagViewModel.From(tag, view!.ItemCount, localizer));
        }

        HasTags = Tags.Count > 0;
    }

    // Passes on every 100th on to the progress shown (one per batch), so ten thousand
    // items don't post ten thousand updates to the UI thread.
    private sealed class CountingProgress(IProgress<int> inner, int total) : IProgress<int>
    {
        public void Report(int value)
        {
            if (value % 100 == 0 || value == total)
            {
                inner.Report(value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the bulk edit")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the metadata of the selection")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
