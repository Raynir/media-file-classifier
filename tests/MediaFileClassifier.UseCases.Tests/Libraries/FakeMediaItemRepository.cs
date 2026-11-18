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

    public int ChangedHandlerCount => Changed?.GetInvocationList().Length ?? 0;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<MediaItem?> GetAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaItemSummary>>(
            [.. Enumerable.Reverse(Items).Skip(offset).Take(limit).Select(item => new MediaItemSummary(
                item.Id, item.FileName, item.FileFormat, item.MediaType, item.SizeBytes, item.FileModifiedAt, item.Status, item.StatusReason, null, false, item.IsFavorite))]);

    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesByIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaItemSummary>>(
            [.. Items.Where(item => ids.Contains(item.Id)).Select(item => new MediaItemSummary(
                item.Id, item.FileName, item.FileFormat, item.MediaType, item.SizeBytes, item.FileModifiedAt, item.Status, item.StatusReason, null, false, item.IsFavorite))]);

    public Task<IReadOnlyList<Id<MediaItem>>> GetFilteredIdsAsync(MediaItemFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Id<MediaItem>>>([.. Enumerable.Reverse(Items).Select(item => item.Id)]);

    public Task<int> CountNewerAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Count - 1 - Items.FindIndex(item => item.Id == id));

    public Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest = null, CancellationToken cancellationToken = default)
    {
        var start = newest is { } id ? Items.Count - 1 - Items.FindIndex(item => item.Id == id) : 0;
        return Task.FromResult<IReadOnlyList<Id<MediaItem>>>([.. Enumerable.Reverse(Items).Skip(start + offset).Take(limit).Select(item => item.Id)]);
    }

    public Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Id<MediaItem>>>(Items.Select(item => item.Id).Where(ids.Contains).ToHashSet());

    public Task<IReadOnlyList<MediaItemPath>> GetPathsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaItemPath>>(
            [.. Items.Where(item => ids.Contains(item.Id)).Select(item => new MediaItemPath(item.Id, item.FolderId, item.Path, item.Status))]);

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

    public Task<IReadOnlyList<MediaItem>> GetByFolderAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaItem>>([.. Items.Where(item => item.FolderId == folderId)]);

    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(MediaItemFilter filter, int offset, int limit, CancellationToken cancellationToken = default) =>
        GetSummariesAsync(offset, limit, cancellationToken);

    public Task<int> CountNewerAsync(MediaItemFilter filter, Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        CountNewerAsync(id, cancellationToken);

    public Task<int> CountAsync(MediaItemFilter filter, CancellationToken cancellationToken = default) => CountAsync(cancellationToken);

    public Task<IReadOnlyList<(string Name, int Count)>> GetSubfoldersAsync(Id<IndexedFolder> folderId, string subfolder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<FolderItemCount>> CountByFolderAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public List<MediaItem[]> UpdatedRanges { get; } = [];

    public Task UpdateRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default)
    {
        UpdatedRanges.Add([.. items]);
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Id<MediaItem>>> DeleteFromFolderAsync(Id<IndexedFolder> folderId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<Id<MediaItem>>> DeleteMissingAsync(int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
