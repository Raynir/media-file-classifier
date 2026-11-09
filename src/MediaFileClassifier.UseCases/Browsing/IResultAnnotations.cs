using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.UseCases.Browsing;

/// <summary>
/// Why a result is in the list (E09-S01-T04).
/// </summary>
/// <param name="Fields">The fields the keyword search matched; <see cref="SearchFields.None"/> for a result only the
/// semantic search found.</param>
/// <param name="IsSemantic">Whether the semantic search found it too.</param>
/// <param name="IsLessRelevant">Whether it falls below the relevance threshold and is only shown on request.</param>
public sealed record ResultMatch(SearchFields Fields, bool IsSemantic, bool IsLessRelevant);

/// <summary>
/// Implemented by a result source that can say why it returned a result. The browser asks it for the tiles on screen.
/// </summary>
public interface IResultAnnotations
{
    /// <summary>
    /// Gets why an item is in the results, for the pages loaded so far.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The reason, or <see langword="null"/> for browsing or an item that isn't loaded.</returns>
    ResultMatch? GetMatch(Id<MediaItem> itemId);
}
