using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// Published when an imported item is registered: its metadata and thumbnails are stored and it's Pending analysis
/// (E03-S07-T05). AI analysis subscribes in M2 (E06-S06-T01).
/// </summary>
/// <remarks>Handlers run on the job's thread-pool thread, so UI handlers dispatch to the UI thread themselves.</remarks>
/// <param name="ItemId">The registered item.</param>
/// <param name="FolderId">The folder it's in.</param>
/// <param name="ContentChanged">
/// Whether the item was registered before and its file's content changed (E02-S04-T03), so its AI results are out of date.
/// Analysis runs again; user values stay (D020).
/// </param>
public sealed record MediaItemRegistered(Id<MediaItem> ItemId, Id<IndexedFolder> FolderId, bool ContentChanged = false) : INotification;
