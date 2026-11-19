using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.SuggestTags;

/// <summary>
/// A tag the tag editor offers while the user types (E07-S02-T02).
/// </summary>
/// <param name="Text">The tag.</param>
/// <param name="ItemCount">How many items show it.</param>
public sealed record TagSuggestionItem(string Text, int ItemCount);

/// <summary>
/// Gets the tags in the library that start with what the user has typed, most used first, for the tag editor's
/// autocompletion. A tag also matches by its other-language form (D382).
/// </summary>
/// <param name="Prefix">The text typed so far, or <see langword="null"/> or empty for the most used tags.</param>
/// <param name="Limit">The most tags to return.</param>
public sealed record SuggestTagsQuery(string? Prefix, int Limit = 8) : IRequest<IReadOnlyList<TagSuggestionItem>>;

/// <summary>
/// Handles <see cref="SuggestTagsQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class SuggestTagsQueryHandler(ILibrarySession session) : IRequestHandler<SuggestTagsQuery, IReadOnlyList<TagSuggestionItem>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TagSuggestionItem>> Handle(SuggestTagsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.Limit <= 0)
        {
            return [];
        }

        var tags = await library.Metadata.GetEffectiveTagsAsync(request.Prefix?.Trim(), request.Limit, cancellationToken).ConfigureAwait(false);
        return [.. tags.Select(tag => new TagSuggestionItem(tag.Tag.Value, tag.ItemCount))];
    }
}
