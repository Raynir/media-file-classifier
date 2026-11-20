using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// One tag in the bulk editor (E07-S03-T01): the tag with how many of the selected items show it, e.g. "dog (37 of 120)",
/// and what Save will do with it: add it to every item, remove it, or reject it (D404).
/// </summary>
public sealed partial class BulkTagViewModel : ObservableObject
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkTagViewModel"/> class.
    /// </summary>
    /// <param name="text">The tag.</param>
    /// <param name="itemCount">The number of selected items that show it.</param>
    /// <param name="total">The number of selected items.</param>
    /// <param name="userCount">The number of items that have it as a user tag.</param>
    /// <param name="aiCount">The number of items that show it from an AI run.</param>
    /// <param name="stage">What Save will do with it.</param>
    /// <param name="localizer">UI strings.</param>
    public BulkTagViewModel(string text, int itemCount, int total, int userCount, int aiCount, TagStage stage, Localizer localizer)
    {
        Text = text;
        ItemCount = itemCount;
        Total = total;
        UserCount = userCount;
        AiCount = aiCount;
        Stage = stage;
        this.localizer = localizer;
    }

    /// <summary>
    /// Gets the tag.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the number of selected items that show the tag.
    /// </summary>
    public int ItemCount { get; }

    /// <summary>
    /// Gets the number of selected items.
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// Gets the number of items that have the tag as a user tag.
    /// </summary>
    public int UserCount { get; }

    /// <summary>
    /// Gets the number of items that show the tag from an AI run.
    /// </summary>
    public int AiCount { get; }

    /// <summary>
    /// Gets or sets what Save will do with the tag.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove), nameof(CanReject), nameof(IsStaged), nameof(IsAdded), nameof(IsDropped), nameof(Label), nameof(CanUndo))]
    public partial TagStage Stage { get; set; }

    /// <summary>
    /// Gets the tag with its count: "dog (37 of 120)", or what is staged for a tag typed in.
    /// </summary>
    public string Label => Stage == TagStage.Added && ItemCount == 0
        ? localizer.Format("Bulk_Tag_AddedLabel", Text, Format.Number(Total))
        : localizer.Format("Bulk_Tag_Label", Text, Format.Number(ItemCount), Format.Number(Total));

    /// <summary>
    /// Gets a value indicating whether the tag can be removed from the items that have it as a user tag.
    /// </summary>
    public bool CanRemove => (Stage == TagStage.Unchanged && UserCount > 0) || (Stage == TagStage.Added && ItemCount == 0);

    /// <summary>
    /// Gets a value indicating whether the tag can be rejected on the items that show it.
    /// </summary>
    public bool CanReject => Stage == TagStage.Unchanged && ItemCount > 0;

    /// <summary>
    /// Gets a value indicating whether a change is staged on the tag.
    /// </summary>
    public bool IsStaged => Stage != TagStage.Unchanged;

    /// <summary>
    /// Gets a value indicating whether the tag is staged to be added.
    /// </summary>
    public bool IsAdded => Stage == TagStage.Added;

    /// <summary>
    /// Gets a value indicating whether the tag is staged to be removed or rejected, for the strikethrough.
    /// </summary>
    public bool IsDropped => Stage is TagStage.Removed or TagStage.Rejected;

    /// <summary>
    /// Gets a value indicating whether the staged change can be taken back.
    /// </summary>
    public bool CanUndo => IsDropped || (Stage == TagStage.Added && ItemCount > 0);

    /// <summary>
    /// Builds the chip of a tag the selection shows.
    /// </summary>
    /// <param name="view">The tag's aggregate.</param>
    /// <param name="total">The number of selected items.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The chip.</returns>
    internal static BulkTagViewModel From(BulkTagView view, int total, Localizer localizer) =>
        new(view.Tag.Value, view.ItemCount, total, view.UserCount, view.AiCount, TagStage.Unchanged, localizer);
}
