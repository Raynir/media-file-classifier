using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.ItemDetails;

/// <summary>
/// What the details panel shows about an item (E14-S05): its file, technical metadata and read-only metadata.
/// </summary>
/// <param name="Id">The item.</param>
/// <param name="FileName">The file name with its extension.</param>
/// <param name="FileFormat">The file extension without the dot, e.g. <c>jpg</c>.</param>
/// <param name="MediaType">Image, animated image or video.</param>
/// <param name="SizeBytes">The file size.</param>
/// <param name="FileModifiedAt">When the file last changed, which also versions its thumbnails.</param>
/// <param name="ImportedAt">When the library found the file.</param>
/// <param name="Status">The processing status.</param>
/// <param name="StatusReason">Why the item has its status, e.g. an error message.</param>
/// <param name="Location">Where the file is, and whether it can be used.</param>
/// <param name="Technical">The extracted technical metadata, or <see langword="null"/> before extraction.</param>
/// <param name="Title">The effective title, or <see langword="null"/>.</param>
/// <param name="Description">The effective description, or <see langword="null"/>.</param>
/// <param name="Tags">The effective tags.</param>
public sealed record ItemDetails(
    Id<MediaItem> Id,
    string FileName,
    string FileFormat,
    MediaType MediaType,
    long SizeBytes,
    DateTimeOffset FileModifiedAt,
    DateTimeOffset ImportedAt,
    MediaItemStatus Status,
    string? StatusReason,
    ItemLocation.ItemLocation Location,
    TechnicalMetadata? Technical,
    string? Title,
    string? Description,
    IReadOnlyList<string> Tags);

/// <summary>
/// Gets what the details panel shows about an item (E14-S05).
/// </summary>
/// <param name="ItemId">The item.</param>
public sealed record GetItemDetailsQuery(Id<MediaItem> ItemId) : IRequest<ItemDetails?>;

/// <summary>
/// Handles <see cref="GetItemDetailsQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemDetailsQueryHandler(ILibrarySession session) : IRequestHandler<GetItemDetailsQuery, ItemDetails?>
{
    /// <inheritdoc />
    public async Task<ItemDetails?> Handle(GetItemDetailsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library
            || await library.Items.GetAsync(request.ItemId, cancellationToken).ConfigureAwait(false) is not { } item
            || await library.Folders.GetAsync(item.FolderId, cancellationToken).ConfigureAwait(false) is not { } folder)
        {
            return null;
        }

        var technical = await library.Items.GetTechnicalMetadataAsync(item.Id, cancellationToken).ConfigureAwait(false);
        var metadata = await library.Metadata.GetAsync(item.Id, cancellationToken).ConfigureAwait(false);
        return new ItemDetails(
            item.Id,
            item.Path.FileName,
            item.FileFormat,
            item.MediaType,
            item.SizeBytes,
            item.FileModifiedAt,
            item.ImportedAt,
            item.Status,
            item.StatusReason,
            GetItemLocationsQueryHandler.Locate(item.Id, item.Path, item.Status, folder),
            technical,
            metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text,
            metadata.GetEffectiveValue(DescriptiveField.Description)?.Value.Text,
            [.. metadata.GetEffectiveTags().Select(tag => tag.Tag.Value)]);
    }
}
