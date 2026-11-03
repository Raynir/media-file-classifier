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
public sealed record MediaItemRegistered(Id<MediaItem> ItemId, Id<IndexedFolder> FolderId) : INotification;
