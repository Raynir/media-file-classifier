using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetItemMetadata;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The metadata editor in the details panel (E07-S02): the focused item's descriptive and AI fields and its tags, as a
/// working copy that changes only when the user saves (D019).
/// </summary>
public sealed partial class MetadataEditorViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private int loads;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataEditorViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the use cases.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logs a load that failed.</param>
    public MetadataEditorViewModel(ISender sender, Localizer localizer, ILogger? logger = null)
    {
        this.sender = sender;
        this.localizer = localizer;
        this.logger = logger ?? NullLogger.Instance;
        Descriptive =
        [
            Create(DescriptiveField.Title, "Details_Title_Label"),
            Create(DescriptiveField.Description, "Details_Description"),
            Create(DescriptiveField.Summary, "Editor_Field_Summary"),
            Create(DescriptiveField.Notes, "Editor_Field_Notes"),
        ];
        Analysis =
        [
            Create(DescriptiveField.Objects, "Editor_Field_Objects"),
            Create(DescriptiveField.Scenes, "Editor_Field_Scenes"),
            Create(DescriptiveField.Actions, "Editor_Field_Actions"),
            Create(DescriptiveField.Emotions, "Editor_Field_Emotions"),
            Create(DescriptiveField.OcrText, "Editor_Field_OcrText"),
        ];
        Transcript = Create(DescriptiveField.Transcript, "Editor_Field_Transcript", isReadOnly: true);
        localizer.Changed += (_, _) => RefreshLabels();
    }

    /// <summary>
    /// Gets the title, description, summary and notes.
    /// </summary>
    public IReadOnlyList<EditableFieldViewModel> Descriptive { get; }

    /// <summary>
    /// Gets the AI analysis fields: objects, scenes, actions, emotions and OCR text.
    /// </summary>
    public IReadOnlyList<EditableFieldViewModel> Analysis { get; }

    /// <summary>
    /// Gets the transcript, shown read-only when the item has one.
    /// </summary>
    public EditableFieldViewModel Transcript { get; }

    /// <summary>
    /// Gets the item's tags.
    /// </summary>
    public ObservableCollection<TagChipViewModel> Tags { get; } = [];

    /// <summary>
    /// Gets the item being edited, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial Id<MediaItem>? ItemId { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the item has tags.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTags { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the item has a transcript.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTranscript { get; private set; }

    /// <summary>
    /// Gets a value indicating whether anything is staged and not saved.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a save is running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; private set; }

    /// <summary>
    /// Gets a message about a save that failed for a reason other than a field's value, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    /// <summary>
    /// Gets a task for the latest load, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    private IEnumerable<EditableFieldViewModel> AllFields => [.. Descriptive, .. Analysis];

    /// <summary>
    /// Shows an item's metadata. The staged changes of the item shown before are dropped.
    /// </summary>
    /// <param name="itemId">The item, or <see langword="null"/> for none.</param>
    /// <returns>A task that completes when the fields are filled.</returns>
    public Task LoadAsync(Id<MediaItem>? itemId) => Loading = LoadCoreAsync(itemId);

    /// <summary>
    /// Drops every staged change.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        foreach (var field in AllFields)
        {
            field.Discard();
        }

        SaveError = null;
        UpdateDirty();
    }

    /// <summary>
    /// Saves every staged change to the item as one edit (D019). A value the service refuses is shown next to its
    /// field, nothing is written, and the staged changes stay.
    /// </summary>
    /// <returns>A task that completes when the save has finished.</returns>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        if (ItemId is not { } id)
        {
            return;
        }

        var edits = BuildEdits();
        if (edits.Count == 0)
        {
            return;
        }

        IsSaving = true;
        SaveError = null;
        try
        {
            var result = await sender.Send(new EditMetadataCommand([id], edits));
            if (result.Error is { } error)
            {
                ShowError(error);
                return;
            }

            // The saved values come back from the library, so the editor shows what is stored.
            if (ItemId == id)
            {
                await LoadCoreAsync(id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(logger, ex);
            SaveError = localizer.Get("Editor_Error_SaveFailed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Lists the edits that make the item what the editor shows: nothing for a field that didn't change.
    /// </summary>
    /// <returns>The edits, in the order the service applies them.</returns>
    internal List<MetadataEdit> BuildEdits()
    {
        var edits = new List<MetadataEdit>();
        foreach (var field in AllFields.Where(field => field.IsDirty))
        {
            edits.Add(field.IsReverted
                ? new MetadataEdit.RevertField(field.Field)
                : new MetadataEdit.SetField(field.Field, field.ToValue()));
        }

        return edits;
    }

    private bool CanSave() => IsDirty && !IsSaving;

    private void ShowError(MetadataValidationError error)
    {
        var field = AllFields.FirstOrDefault(candidate => candidate.Field == error.Field);
        var message = localizer.Format("Editor_Error_" + error.Code, error.Limit ?? 0, error.Tag ?? string.Empty);
        if (field is null)
        {
            SaveError = message;
        }
        else
        {
            field.Error = message;
        }
    }

    private EditableFieldViewModel Create(DescriptiveField field, string labelKey, bool isReadOnly = false)
    {
        var item = new EditableFieldViewModel(field, labelKey, localizer.Get(labelKey), isReadOnly);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditableFieldViewModel.IsDirty))
            {
                UpdateDirty();
            }
        };
        return item;
    }

    private void RefreshLabels()
    {
        foreach (var field in AllFields.Append(Transcript))
        {
            field.Label = localizer.Get(field.LabelKey);
        }
    }

    private void UpdateDirty()
    {
        IsDirty = AllFields.Any(field => field.IsDirty);
        SaveCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCoreAsync(Id<MediaItem>? itemId)
    {
        var load = ++loads;
        ItemId = itemId;
        ItemMetadataView? view = null;
        if (itemId is { } id)
        {
            try
            {
                view = await sender.Send(new GetItemMetadataQuery(id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoadFailed(logger, ex);
            }
        }

        if (load != loads)
        {
            return;
        }

        Apply(view);
    }

    private void Apply(ItemMetadataView? view)
    {
        foreach (var field in AllFields.Append(Transcript))
        {
            field.Load(view is null ? new FieldMetadataView(field.Field, null, null, null, null, null) : view[field.Field]);
        }

        HasTranscript = Transcript.HasText;
        Tags.Clear();
        foreach (var tag in view?.Tags ?? [])
        {
            Tags.Add(new TagChipViewModel(tag.Tag.Tag.Value, tag.Tag.Source, tag.Tag.Confidence));
        }

        HasTags = Tags.Count > 0;
        UpdateDirty();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the metadata edits")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the metadata to edit")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
