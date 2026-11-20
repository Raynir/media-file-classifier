using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata;

/// <summary>
/// What kind of user value changed on items. One change can be several kinds, e.g. a Save that edits a field and a tag.
/// </summary>
[Flags]
public enum ItemMetadataChange
{
    /// <summary>The favorite mark changed.</summary>
    Favorite = 1,

    /// <summary>A descriptive field changed: its value, its user layer or its suggestion flag.</summary>
    Fields = 2,

    /// <summary>The tags changed: added, removed, rejected, confirmed, or their suggestion flags.</summary>
    Tags = 4,

    /// <summary>The analysis of the item ran: its keyframes, their pictures and the analyzers' results may have changed.</summary>
    Analysis = 8,
}

/// <summary>
/// Published after user values of items changed (D370): favorites (E11-S01-T01), and every write of the metadata
/// service (E07-S01-T03), so the grid, the details panel, the editors and the filters can update in place. A command
/// publishes it once, with every item it changed, so a bulk edit refreshes the views once (MFC-419).
/// </summary>
/// <remarks>Handlers run on the caller's thread, so UI handlers dispatch to the UI thread themselves.</remarks>
/// <param name="ItemIds">The items whose value changed.</param>
/// <param name="Change">What changed.</param>
/// <param name="IsFavorite">For <see cref="ItemMetadataChange.Favorite"/>: the new value, the same for all items.</param>
public sealed record ItemsMetadataChanged(IReadOnlyList<Id<MediaItem>> ItemIds, ItemMetadataChange Change, bool IsFavorite = false) : INotification
{
    /// <summary>
    /// Gets the fields that changed, for <see cref="ItemMetadataChange.Fields"/>.
    /// </summary>
    public IReadOnlySet<DescriptiveField> Fields { get; init; } = new HashSet<DescriptiveField>();
}
