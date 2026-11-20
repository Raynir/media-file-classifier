using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// What the editor will do with a tag when the user saves (D019).
/// </summary>
public enum TagStage
{
    /// <summary>The tag is as stored.</summary>
    Unchanged,

    /// <summary>The user typed the tag; it isn't stored yet.</summary>
    Added,

    /// <summary>The user removed the tag; it stays stored until the save.</summary>
    Removed,

    /// <summary>The user rejected the tag; the save keeps AI runs and the file from adding it again.</summary>
    Rejected,
}

/// <summary>
/// One tag chip in the metadata editor (E07-S02): the tag, where it comes from, and the change staged on it.
/// </summary>
public sealed partial class TagChipViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TagChipViewModel"/> class.
    /// </summary>
    /// <param name="text">The tag.</param>
    /// <param name="source">Where the tag comes from.</param>
    /// <param name="confidence">The AI confidence, for an AI tag.</param>
    /// <param name="stage">The staged change; only a tag the user typed starts as <see cref="TagStage.Added"/>.</param>
    /// <param name="suggestion">The tag's new AI suggestion, if any (E07-S11-T03).</param>
    public TagChipViewModel(string text, MetadataSource source, double? confidence = null, TagStage stage = TagStage.Unchanged, TagSuggestionKind? suggestion = null)
    {
        Suggestion = suggestion;
        Text = text;
        Source = source;
        Confidence = confidence;
        Stage = stage;
    }

    /// <summary>
    /// Gets the tag.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets where the tag comes from.
    /// </summary>
    public MetadataSource Source { get; }

    /// <summary>
    /// Gets the AI confidence of an AI tag, or <see langword="null"/>.
    /// </summary>
    public double? Confidence { get; }

    /// <summary>
    /// Gets the tag's new AI suggestion: <see cref="TagSuggestionKind.Added"/> on an AI tag the user doesn't have,
    /// <see cref="TagSuggestionKind.Removed"/> on an accepted tag the AI no longer gives; or <see langword="null"/>.
    /// </summary>
    public TagSuggestionKind? Suggestion { get; }

    /// <summary>
    /// Gets or sets what is staged on the tag's suggestion.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStaged), nameof(CanDecideSuggestion), nameof(IsSuggestionAccepted), nameof(IsSuggestionDismissed), nameof(WillBeDropped), nameof(HasStagedSuggestion))]
    public partial SuggestionStage SuggestionStaged { get; set; }

    /// <summary>
    /// Gets a value indicating whether the suggestion can still be accepted or dismissed: the tag has one, and no change
    /// is staged on the tag or the suggestion.
    /// </summary>
    public bool CanDecideSuggestion => Suggestion is not null && Stage == TagStage.Unchanged && SuggestionStaged == SuggestionStage.None;

    /// <summary>
    /// Gets a value indicating whether the AI now suggests adding this tag.
    /// </summary>
    public bool IsSuggestedAdd => Suggestion == TagSuggestionKind.Added;

    /// <summary>
    /// Gets a value indicating whether the AI no longer gives this tag.
    /// </summary>
    public bool IsSuggestedRemoval => Suggestion == TagSuggestionKind.Removed;

    /// <summary>
    /// Gets a value indicating whether accepting the suggestion is staged.
    /// </summary>
    public bool IsSuggestionAccepted => SuggestionStaged == SuggestionStage.Accepted;

    /// <summary>
    /// Gets a value indicating whether dismissing the suggestion is staged.
    /// </summary>
    public bool IsSuggestionDismissed => SuggestionStaged == SuggestionStage.Dismissed;

    /// <summary>
    /// Gets a value indicating whether an accept or dismiss is staged on the suggestion.
    /// </summary>
    public bool HasStagedSuggestion => SuggestionStaged != SuggestionStage.None;

    /// <summary>
    /// Gets a value indicating whether the tag goes away on save: the user accepted the AI no longer giving it.
    /// </summary>
    public bool WillBeDropped => SuggestionStaged == SuggestionStage.Accepted && Suggestion == TagSuggestionKind.Removed;

    /// <summary>
    /// Gets a value indicating whether the user has this tag (typed, or accepted from the AI).
    /// </summary>
    public bool IsUser => Source == MetadataSource.User;

    /// <summary>
    /// Gets or sets the change staged on the tag.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded), nameof(IsRemoved), nameof(IsRejected), nameof(IsStaged), nameof(CanRemove), nameof(CanReject), nameof(CanDecideSuggestion))]
    public partial TagStage Stage { get; set; }

    /// <summary>
    /// Gets a value indicating whether the tag was typed and isn't saved.
    /// </summary>
    public bool IsAdded => Stage == TagStage.Added;

    /// <summary>
    /// Gets a value indicating whether the tag is staged for removal.
    /// </summary>
    public bool IsRemoved => Stage == TagStage.Removed;

    /// <summary>
    /// Gets a value indicating whether the tag is staged for rejection.
    /// </summary>
    public bool IsRejected => Stage == TagStage.Rejected;

    /// <summary>
    /// Gets a value indicating whether a change is staged on the tag.
    /// </summary>
    public bool IsStaged => Stage != TagStage.Unchanged || SuggestionStaged != SuggestionStage.None;

    /// <summary>
    /// Gets a value indicating whether the tag can be removed: a tag the user has, which no change is staged on.
    /// </summary>
    public bool CanRemove => Stage != TagStage.Removed && Stage != TagStage.Rejected && (IsUser || Stage == TagStage.Added) && !WillBeDropped;

    /// <summary>
    /// Gets a value indicating whether the tag can be rejected: any tag no change is staged on.
    /// </summary>
    public bool CanReject => Stage is TagStage.Unchanged or TagStage.Added;
}
