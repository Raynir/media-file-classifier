using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Media items kept in a list, newest last. Only what the browsing use cases need is implemented.
/// </summary>
internal sealed class FakeMediaItemRepository : IMediaItemRepository
{
    public event EventHandler? Changed;

    public List<MediaItem> Items { get; } = [];

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<MediaItem?> GetAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaItemSummary>>(
            [.. Enumerable.Reverse(Items).Skip(offset).Take(limit).Select(item => new MediaItemSummary(
                item.Id, item.FileName, item.FileFormat, item.MediaType, item.SizeBytes, item.FileModifiedAt, item.Status, item.StatusReason, null, false))]);

    public Task<int> CountNewerAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Count - 1 - Items.FindIndex(item => item.Id == id));

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items.Count);

    public Task<MediaItem?> FindByPathAsync(Id<IndexedFolder> folderId, RelativePath path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItemFileState>> GetFileStatesAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItem>> FindByContentHashAsync(UInt128 contentHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItem>> FindByFingerprintAsync(UInt128 fingerprint, long sizeBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItemIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItem>> GetWithoutContentHashAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItem>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<MediaItem>> GetByStatusAsync(MediaItemStatus status, Id<IndexedFolder>? folderId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ExactDuplicateGroup>> GetExactDuplicatesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<TechnicalMetadata?> GetTechnicalMetadataAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task UpdateAsync(MediaItem item, TechnicalMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task AddAsync(MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task AddRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task UpdateAsync(MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
