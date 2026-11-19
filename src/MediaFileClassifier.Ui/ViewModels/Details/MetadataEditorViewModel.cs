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
using MediaFileClassifier.UseCases.Metadata.SuggestTags;
using MediaFileClassifier.UseCases.Tags;
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
    private const int SuggestionLimit = 8;

    private readonly ISender sender;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private int loads;
    private int suggestionRequests;
    private ItemMetadataView? shown;

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
    /// Gets or sets the text typed into the tag input.
    /// </summary>
    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    /// <summary>
    /// Gets the tags offered for the typed text, most used first.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Suggestions { get; private set; } = [];

    /// <summary>
    /// Gets a message about a tag the service refused, shown under the tag input, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? TagError { get; private set; }

    /// <summary>
    /// Gets a task for the latest load, for tests.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Gets a task for the latest suggestion lookup, for tests.
    /// </summary>
    internal Task SuggestionsLoading { get; private set; } = Task.CompletedTask;

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

        ApplyTags(shown);
        NewTagText = string.Empty;
        TagError = null;
        SaveError = null;
        UpdateDirty();
    }

    /// <summary>
    /// Stages a tag the user typed: it is added when the user saves. A tag the item shows already is left as it is,
    /// unless a removal or rejection of it is staged, which is undone. Tags compare ignoring case.
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
            AddChip(new TagChipViewModel(clean, MetadataSource.User, stage: TagStage.Added));
            UpdateDirty();
        }
        else if (existing.Stage is TagStage.Removed or TagStage.Rejected)
        {
            existing.Stage = TagStage.Unchanged;
        }

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
    /// Stages the removal of a tag the user has. A tag typed and not saved is dropped.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void RemoveTag(TagChipViewModel? chip) => Stage(chip, TagStage.Removed, chip?.CanRemove == true);

    /// <summary>
    /// Stages the rejection of a tag, so AI runs and the file don't add it again. A tag typed and not saved is dropped.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void RejectTag(TagChipViewModel? chip) => Stage(chip, TagStage.Rejected, chip?.CanReject == true);

    /// <summary>
    /// Takes back the removal or rejection staged on a tag.
    /// </summary>
    /// <param name="chip">The tag.</param>
    [RelayCommand]
    public void UndoTag(TagChipViewModel? chip) => Stage(chip, TagStage.Unchanged, chip is { Stage: TagStage.Removed or TagStage.Rejected });

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

        // Removals and rejections go first, so a tag staged for removal and typed again nets out in the service.
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Removed).Select(chip => (MetadataEdit)new MetadataEdit.RemoveTag(TagName.From(chip.Text))));
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Rejected).Select(chip => (MetadataEdit)new MetadataEdit.RejectTag(TagName.From(chip.Text))));
        edits.AddRange(Tags.Where(chip => chip.Stage == TagStage.Added).Select(chip => (MetadataEdit)new MetadataEdit.AddTag(chip.Text)));
        return edits;
    }

    partial void OnNewTagTextChanged(string value)
    {
        TagError = null;
        SuggestionsLoading = LoadSuggestionsAsync(value);
    }

    private void Stage(TagChipViewModel? chip, TagStage stage, bool allowed)
    {
        if (chip is null || !allowed)
        {
            return;
        }

        if (chip.Stage == TagStage.Added && stage != TagStage.Unchanged)
        {
            // A tag that was never saved has nothing to remove or reject.
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

    private void AddChip(TagChipViewModel chip)
    {
        chip.PropertyChanged += OnChipChanged;
        Tags.Add(chip);
        HasTags = true;
    }

    private void OnChipChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagChipViewModel.Stage))
        {
            UpdateDirty();
        }
    }

    private async Task LoadSuggestionsAsync(string text)
    {
        var request = ++suggestionRequests;
        IReadOnlyList<TagSuggestionItem> found;
        try
        {
            found = await sender.Send(new SuggestTagsQuery(text, SuggestionLimit + Tags.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSuggestFailed(logger, ex);
            found = [];
        }

        if (request != suggestionRequests)
        {
            return;
        }

        var shownTags = Tags.Where(chip => chip.Stage is not (TagStage.Removed or TagStage.Rejected)).Select(chip => chip.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Suggestions = [.. found.Select(tag => tag.Text).Where(tag => !shownTags.Contains(tag)).Take(SuggestionLimit)];
    }

    private bool CanSave() => IsDirty && !IsSaving;

    private void ShowError(MetadataValidationError error)
    {
        var field = AllFields.FirstOrDefault(candidate => candidate.Field == error.Field);
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
        IsDirty = AllFields.Any(field => field.IsDirty) || Tags.Any(chip => chip.IsStaged);
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
        shown = view;
        ApplyTags(view);
        NewTagText = string.Empty;
        TagError = null;
        UpdateDirty();
    }

    private void ApplyTags(ItemMetadataView? view)
    {
        foreach (var chip in Tags)
        {
            chip.PropertyChanged -= OnChipChanged;
        }

        Tags.Clear();
        foreach (var tag in view?.Tags ?? [])
        {
            AddChip(new TagChipViewModel(tag.Tag.Tag.Value, tag.Tag.Source, tag.Tag.Confidence));
        }

        HasTags = Tags.Count > 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the metadata edits")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't look up tags to suggest")]
    private static partial void LogSuggestFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the metadata to edit")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
