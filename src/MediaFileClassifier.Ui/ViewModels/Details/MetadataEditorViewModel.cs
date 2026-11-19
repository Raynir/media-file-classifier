using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata;
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

        UpdateDirty();
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

    private void UpdateDirty() => IsDirty = AllFields.Any(field => field.IsDirty);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the metadata to edit")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
