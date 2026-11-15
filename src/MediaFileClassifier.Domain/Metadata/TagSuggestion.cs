using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// A "new AI suggestion available" flag on a tag (D020, E06-S07-T04). The user accepts or dismisses it.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Tag">The tag.</param>
/// <param name="RunId">The run that made the suggestion.</param>
/// <param name="Kind">Whether the run adds or drops the tag.</param>
public sealed record TagSuggestion(Id<MediaItem> ItemId, TagName Tag, Id<AnalysisRun> RunId, TagSuggestionKind Kind)
{
    private TagSuggestion()
        : this(default, null!, default, default)
    {
    }
}
