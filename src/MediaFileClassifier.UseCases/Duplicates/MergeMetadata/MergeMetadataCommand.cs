using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.MergeMetadata;

/// <summary>
/// Merges the user's metadata of duplicates into the item kept (E12-S04-T02, spec 1.8), before the duplicates are removed
/// from the library: user tags are combined, the keeper becomes a favorite if any member was one, and user field values
/// follow <see cref="MetadataMerge"/> (the keeper's value wins, a field only members have is copied, other values are
/// reported as conflicts). AI values are not merged. The members are not changed. Collection memberships are merged once
/// collections exist (E11-S02).
/// </summary>
/// <param name="KeeperId">The item kept.</param>
/// <param name="MemberIds">The other members, in order of preference. The keeper and unknown items are ignored.</param>
/// <param name="Choices">The value the user picked per conflicting field (E12-S04 resolve dialog); none keeps the keeper's values.</param>
public sealed record MergeMetadataCommand(
    Id<MediaItem> KeeperId,
    IReadOnlyList<Id<MediaItem>> MemberIds,
    IReadOnlyDictionary<DescriptiveField, FieldValue>? Choices = null) : IRequest<MergeMetadataResult>;

/// <summary>
/// The outcome of <see cref="MergeMetadataCommand"/>.
/// </summary>
/// <param name="Outcome">What changed in the keeper's metadata, and the conflicts.</param>
/// <param name="FavoriteAdded">Whether the keeper became a favorite.</param>
/// <param name="TooManyTags">Whether the keeper now has more user tags than <see cref="MetadataLimits.UserTagsPerItem"/>.
/// The merge keeps them all; adding another tag to the keeper is refused until some are removed.</param>
/// <param name="Undo">What the keeper had before, to put back (E12-S04-T06), or <see langword="null"/> if nothing changed.</param>
public sealed record MergeMetadataResult(MetadataMergeOutcome Outcome, bool FavoriteAdded, bool TooManyTags, MetadataMergeUndo? Undo)
{
    /// <summary>
    /// Gets the result of a merge that changed nothing: no library, no keeper or no member.
    /// </summary>
    public static MergeMetadataResult None { get; } = new(new MetadataMergeOutcome([], [], []), false, false, null);

    /// <summary>
    /// Gets exactly what the merge changed on the keeper, so an undo reverts only that (E12-S04-T06), or
    /// <see langword="null"/> if nothing changed.
    /// </summary>
    public MetadataMergeRecord? Record { get; init; }
}

/// <summary>
/// What the keeper of a metadata merge had before it, so the merge can be undone: its user layer (put back with
/// <see cref="ItemMetadata.RestoreUserLayer"/>) and its favorite mark.
/// </summary>
/// <param name="KeeperId">The keeper.</param>
/// <param name="UserLayer">The keeper's user layer before the merge.</param>
/// <param name="WasFavorite">Whether the keeper was a favorite before the merge.</param>
public sealed record MetadataMergeUndo(Id<MediaItem> KeeperId, UserLayerSnapshot UserLayer, bool WasFavorite);

/// <summary>
/// Handles <see cref="MergeMetadataCommand"/> for the open library and publishes <see cref="ItemsMetadataChanged"/> for the
/// keeper.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="clock">The clock, for the change time.</param>
/// <param name="publisher">Publishes the change.</param>
public sealed class MergeMetadataCommandHandler(ILibrarySession session, TimeProvider clock, IPublisher publisher)
    : IRequestHandler<MergeMetadataCommand, MergeMetadataResult>
{
    /// <inheritdoc />
    public async Task<MergeMetadataResult> Handle(MergeMetadataCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memberIds = request.MemberIds.Where(id => id != request.KeeperId).Distinct().ToList();
        if (session.Current is not { } library || memberIds.Count == 0)
        {
            return MergeMetadataResult.None;
        }

        var summaries = (await library.Items.GetSummariesByIdsAsync([request.KeeperId, .. memberIds], cancellationToken).ConfigureAwait(false))
            .ToDictionary(summary => summary.Id);
        memberIds.RemoveAll(id => !summaries.ContainsKey(id));
        if (!summaries.TryGetValue(request.KeeperId, out var keeper) || memberIds.Count == 0)
        {
            return MergeMetadataResult.None;
        }

        var loaded = await library.Metadata.GetManyAsync(memberIds, cancellationToken).ConfigureAwait(false);
        var members = memberIds.Select(id => loaded[id]).ToList();
        var now = clock.GetUtcNow();

        UserLayerSnapshot? before = null;
        var (metadata, outcome) = await library.Metadata.UpdateAsync(
            request.KeeperId,
            keeperMetadata =>
            {
                before = keeperMetadata.CaptureUserLayer();
                return MetadataMerge.Apply(keeperMetadata, members, request.Choices, now);
            },
            cancellationToken).ConfigureAwait(false);

        // Not cancellable from here on: the metadata is written, so the caller must get its undo snapshot.
        var favoriteAdded = false;
        if (!keeper.IsFavorite && memberIds.Any(id => summaries[id].IsFavorite))
        {
            var changed = await library.Items.SetFavoriteAsync([request.KeeperId], true, now, CancellationToken.None).ConfigureAwait(false);
            favoriteAdded = changed.Count > 0;
        }

        await PublishAsync(request.KeeperId, outcome, favoriteAdded).ConfigureAwait(false);

        var userTags = metadata.Tags.Where(tag => tag.Source == MetadataSource.User).Select(tag => tag.Tag).Distinct().Count();
        var undo = outcome.Changed || favoriteAdded ? new MetadataMergeUndo(request.KeeperId, before!, keeper.IsFavorite) : null;
        var record = MetadataMergeRecord.From(before!, metadata, outcome, favoriteAdded);
        return new MergeMetadataResult(outcome, favoriteAdded, userTags > MetadataLimits.UserTagsPerItem, undo)
        {
            Record = record.IsEmpty ? null : record,
        };
    }

    private async Task PublishAsync(Id<MediaItem> keeperId, MetadataMergeOutcome outcome, bool favoriteAdded)
    {
        var change = (outcome.CopiedFields.Count > 0 ? ItemMetadataChange.Fields : 0) | (outcome.AddedTags.Count > 0 ? ItemMetadataChange.Tags : 0);
        if (change != 0)
        {
            await publisher.Publish(
                new ItemsMetadataChanged([keeperId], change) { Fields = outcome.CopiedFields.ToHashSet() },
                CancellationToken.None).ConfigureAwait(false);
        }

        if (favoriteAdded)
        {
            await publisher.Publish(new ItemsMetadataChanged([keeperId], ItemMetadataChange.Favorite, true), CancellationToken.None).ConfigureAwait(false);
        }
    }
}
