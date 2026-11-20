using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;

/// <summary>
/// Gets what a selection of items shares (E07-S03-T01): for each field the value all items show, or that they differ,
/// and for each tag how many items show it. Reads the items in batches and keeps only the counts, so a large selection
/// loads in a bounded amount of memory.
/// </summary>
/// <param name="ItemIds">The selected items.</param>
public sealed record GetBulkMetadataQuery(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<BulkMetadataView>;

/// <summary>
/// The shared and differing metadata of a selection, as the details panel shows it in bulk mode.
/// </summary>
/// <param name="ItemCount">The number of distinct items counted.</param>
/// <param name="Fields">Every descriptive field, in <see cref="DescriptiveFields.All"/> order.</param>
/// <param name="Tags">The effective tags of any item, most shared first, then by name.</param>
public sealed record BulkMetadataView(int ItemCount, IReadOnlyList<BulkFieldView> Fields, IReadOnlyList<BulkTagView> Tags)
{
    /// <summary>
    /// Gets an empty view, for no items.
    /// </summary>
    public static BulkMetadataView Empty { get; } = new(0, [], []);

    /// <summary>
    /// Gets one field's aggregate.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The aggregate.</returns>
    public BulkFieldView this[DescriptiveField field] => Fields[(int)field];
}

/// <summary>
/// One field over a selection.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Shared">The effective value every item shows, or <see langword="null"/> when the items differ or none
/// has a value.</param>
/// <param name="IsMixed">Whether the items show different values (an item with no value differs from one with a value).</param>
/// <param name="ItemsWithValue">The number of items that have an effective value.</param>
/// <param name="UserValueCount">The number of items whose user layer sets the field.</param>
/// <param name="AiValueCount">The number of items where the AI layer gives the effective value.</param>
/// <param name="SuggestionCount">The number of items with a new AI suggestion for the field.</param>
public sealed record BulkFieldView(
    DescriptiveField Field,
    FieldValue? Shared,
    bool IsMixed,
    int ItemsWithValue,
    int UserValueCount,
    int AiValueCount,
    int SuggestionCount);

/// <summary>
/// One tag over a selection.
/// </summary>
/// <param name="Tag">The tag.</param>
/// <param name="ItemCount">The number of items that show the tag.</param>
/// <param name="UserCount">The number of items that have it as a user tag.</param>
/// <param name="AiCount">The number of items that show it from an AI run.</param>
/// <param name="SuggestionCount">The number of items with a new AI suggestion on the tag.</param>
public sealed record BulkTagView(TagName Tag, int ItemCount, int UserCount, int AiCount, int SuggestionCount);

/// <summary>
/// Handles <see cref="GetBulkMetadataQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetBulkMetadataQueryHandler(ILibrarySession session) : IRequestHandler<GetBulkMetadataQuery, BulkMetadataView>
{
    private const int BatchSize = 500;

    /// <inheritdoc />
    public async Task<BulkMetadataView> Handle(GetBulkMetadataQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (session.Current is not { } library || request.ItemIds.Count == 0)
        {
            return BulkMetadataView.Empty;
        }

        var fields = DescriptiveFields.All.ToDictionary(field => field, _ => new FieldTally());
        var tags = new Dictionary<TagName, TagTally>();
        var count = 0;
        foreach (var batch in request.ItemIds.Distinct().Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await library.Metadata.GetManyAsync(batch, cancellationToken).ConfigureAwait(false);
            foreach (var id in batch)
            {
                var metadata = loaded[id];
                count++;
                foreach (var (field, tally) in fields)
                {
                    tally.Add(metadata, field);
                }

                foreach (var tag in metadata.GetEffectiveTags())
                {
                    if (!tags.TryGetValue(tag.Tag, out var tally))
                    {
                        tags[tag.Tag] = tally = new TagTally();
                    }

                    tally.Add(tag.Source, metadata.GetTagSuggestion(tag.Tag) is not null);
                }
            }
        }

        return new BulkMetadataView(
            count,
            [.. DescriptiveFields.All.Select(field => fields[field].ToView(field, count))],
            [.. tags
                .Select(pair => new BulkTagView(pair.Key, pair.Value.Items, pair.Value.User, pair.Value.Ai, pair.Value.Suggestions))
                .OrderByDescending(tag => tag.ItemCount)
                .ThenBy(tag => tag.Tag.Value, StringComparer.OrdinalIgnoreCase)]);
    }

    private sealed class FieldTally
    {
        private readonly Dictionary<FieldValue, int> values = [];
        private int user;
        private int ai;
        private int suggestions;
        private int withValue;

        public void Add(ItemMetadata metadata, DescriptiveField field)
        {
            var effective = metadata.GetEffectiveValue(field);
            if (effective is not null)
            {
                withValue++;
                values[effective.Value] = values.GetValueOrDefault(effective.Value) + 1;
                if (effective.Source == MetadataSource.AI)
                {
                    ai++;
                }
            }

            if (metadata.GetUserValue(field) is not null)
            {
                user++;
            }

            if (metadata.GetSuggestedValue(field) is not null)
            {
                suggestions++;
            }
        }

        public BulkFieldView ToView(DescriptiveField field, int itemCount)
        {
            // Items with no value differ from items with one, so a field set on some items only is mixed too.
            var isMixed = values.Count > 1 || (values.Count == 1 && withValue < itemCount);
            return new BulkFieldView(field, values.Count == 1 && !isMixed ? values.Keys.Single() : null, isMixed, withValue, user, ai, suggestions);
        }
    }

    private sealed class TagTally
    {
        public int Items { get; private set; }

        public int User { get; private set; }

        public int Ai { get; private set; }

        public int Suggestions { get; private set; }

        public void Add(MetadataSource source, bool hasSuggestion)
        {
            Items++;
            if (source == MetadataSource.User)
            {
                User++;
            }
            else if (source == MetadataSource.AI)
            {
                Ai++;
            }

            if (hasSuggestion)
            {
                Suggestions++;
            }
        }
    }
}
