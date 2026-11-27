using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.Review;

/// <summary>
/// Gets one duplicate group with what the comparison view shows of each member (E12-S03-T02), and the member the
/// keeper rules suggest keeping (E12-S03-T03).
/// </summary>
/// <param name="GroupId">The group.</param>
/// <param name="KeeperOrder">The user's keeper rule order, or <see langword="null"/> for the default.</param>
public sealed record GetDuplicateGroupQuery(Id<DuplicateGroup> GroupId, IReadOnlyList<KeeperRule>? KeeperOrder = null)
    : IRequest<DuplicateGroupDetail?>;

/// <summary>
/// A duplicate group for the comparison view.
/// </summary>
/// <param name="GroupId">The group.</param>
/// <param name="Type">How the members were found to be duplicates.</param>
/// <param name="Members">The members, in item ID order.</param>
/// <param name="Suggestion">The suggested keeper, or <see langword="null"/> when no member can be kept.</param>
public sealed record DuplicateGroupDetail(
    Id<DuplicateGroup> GroupId,
    DuplicateGroupType Type,
    IReadOnlyList<DuplicateMemberDetail> Members,
    KeeperSuggestion? Suggestion);

/// <summary>
/// One member of a duplicate group, as the comparison view shows it. Collections aren't counted in
/// <see cref="UserMetadataCount"/>: they have no repository yet.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="FileName">File name with its extension.</param>
/// <param name="Format">Lower-case extension without the dot.</param>
/// <param name="SizeBytes">File size.</param>
/// <param name="FileModifiedAt">File modification time, which versions the thumbnail.</param>
/// <param name="FileCreatedAt">File creation time.</param>
/// <param name="Width">Width as displayed, if known.</param>
/// <param name="Height">Height as displayed, if known.</param>
/// <param name="DateTaken">When it was taken, if known.</param>
/// <param name="Folder">The folder the file is in.</param>
/// <param name="IsAvailable">Whether the file can be kept: it isn't Missing or Excluded and its folder isn't offline.</param>
/// <param name="TagCount">The tags the user set.</param>
/// <param name="EditedFieldCount">The descriptive fields the user edited.</param>
/// <param name="IsFavorite">Whether the user marked it as a favorite.</param>
public sealed record DuplicateMemberDetail(
    Id<MediaItem> ItemId,
    string FileName,
    string Format,
    long SizeBytes,
    DateTimeOffset FileModifiedAt,
    DateTimeOffset FileCreatedAt,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    string? Folder,
    bool IsAvailable,
    int TagCount,
    int EditedFieldCount,
    bool IsFavorite)
{
    /// <summary>
    /// Gets how much the user added: tags, edited fields and the favorite mark.
    /// </summary>
    public int UserMetadataCount => TagCount + EditedFieldCount + (IsFavorite ? 1 : 0);

    /// <summary>
    /// Gets the number of pixels, or 0 when the size isn't known.
    /// </summary>
    public long Pixels => (long)(Width ?? 0) * (Height ?? 0);

    /// <summary>
    /// Makes the keeper rules' view of the member.
    /// </summary>
    /// <returns>The candidate.</returns>
    public KeeperCandidate ToCandidate() => new(ItemId, UserMetadataCount, Pixels, SizeBytes, Format, FileCreatedAt, IsAvailable);
}

/// <summary>
/// Handles <see cref="GetDuplicateGroupQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetDuplicateGroupQueryHandler(ILibrarySession session) : IRequestHandler<GetDuplicateGroupQuery, DuplicateGroupDetail?>
{
    /// <inheritdoc />
    public async Task<DuplicateGroupDetail?> Handle(GetDuplicateGroupQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library
            || await library.Duplicates.GetAsync(request.GroupId, cancellationToken).ConfigureAwait(false) is not { } group)
        {
            return null;
        }

        var members = await library.Duplicates.GetMembersAsync(group.Id, cancellationToken).ConfigureAwait(false);
        Id<MediaItem>[] ids = [.. members.Select(member => member.ItemId)];
        var summaries = await library.Items.GetSummariesByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        var items = await library.Items.GetByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        var metadata = await library.Metadata.GetManyAsync(ids, cancellationToken).ConfigureAwait(false);

        var details = new List<DuplicateMemberDetail>();
        foreach (var id in ids)
        {
            var summary = summaries.FirstOrDefault(row => row.Id == id);
            var item = items.FirstOrDefault(row => row.Id == id);
            if (summary is null || item is null)
            {
                continue;
            }

            metadata.TryGetValue(id, out var itemMetadata);
            details.Add(new DuplicateMemberDetail(
                id,
                summary.FileName,
                summary.FileFormat,
                summary.SizeBytes,
                summary.FileModifiedAt,
                item.FileCreatedAt,
                summary.Width,
                summary.Height,
                summary.DateTaken,
                summary.Folder,
                summary.Status is not (MediaItemStatus.Missing or MediaItemStatus.Excluded) && !summary.IsFolderOffline,
                itemMetadata?.Tags.Count(tag => tag.Source == MetadataSource.User) ?? 0,
                itemMetadata?.UserValues.Count ?? 0,
                summary.IsFavorite));
        }

        return new DuplicateGroupDetail(group.Id, group.Type, details, KeeperSuggester.Suggest([.. details.Select(detail => detail.ToCandidate())], request.KeeperOrder));
    }
}
