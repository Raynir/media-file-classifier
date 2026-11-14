using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Browsing;
using MediatR;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for <see cref="ResolveAnalysisScopeQueryHandler"/> and <see cref="AnalyzeScopeCommandHandler"/>, against a real
/// library: which items each scope covers and what job each command queues (E06-S06-T02).
/// </summary>
public sealed class AnalyzeScopeTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;
    private ViewSource view = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await SearchTestBed.CreateAsync();
        view = new ViewSource(bed);
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Pending_CoversOnlyPendingItems_AndCountsVideosApart()
    {
        var pending = await AddAsync(1, MediaItemStatus.Pending);
        var pendingVideo = await AddAsync(2, MediaItemStatus.Pending, MediaType.Video);
        await AddAsync(3, MediaItemStatus.Indexed);
        await AddAsync(4, MediaItemStatus.Discovered);

        var items = await ResolveAsync(AnalysisScope.Pending);

        Assert.Equal(new HashSet<Id<MediaItem>> { pending.Id, pendingVideo.Id }, items.ItemIds.ToHashSet());
        Assert.Equal(1, items.Images);
        Assert.Equal(1, items.Videos);
        Assert.Equal(1, items.VideoFrames);
    }

    [Fact]
    public async Task Pending_PagesThroughMoreItemsThanOnePage()
    {
        var ids = new List<Id<MediaItem>>();
        for (var number = 1; number <= 1005; number++)
        {
            ids.Add((await AddAsync(number, MediaItemStatus.Pending)).Id);
        }

        var items = await ResolveAsync(AnalysisScope.Pending);

        Assert.Equal(ids.ToHashSet(), items.ItemIds.ToHashSet());
        Assert.Equal(ids.Count, items.Count);
    }

    [Fact]
    public async Task FolderAndItems_LeaveOutMissingItems()
    {
        var present = await AddAsync(1, MediaItemStatus.Indexed);
        var missing = await AddAsync(2, MediaItemStatus.Missing);

        var folder = await ResolveAsync(AnalysisScope.Folder(bed.Library.Folder.Id));
        var selected = await ResolveAsync(AnalysisScope.Items([present.Id, missing.Id]));

        Assert.Equal([present.Id], folder.ItemIds);
        Assert.Equal([present.Id], selected.ItemIds);
    }

    [Fact]
    public async Task Folder_CoversTheFolderAndTheSubfolderFilter()
    {
        var inAlbum = await AddAsync(1, MediaItemStatus.Indexed);
        var other = await AddAsync(2, MediaItemStatus.Indexed, subfolder: "trip");

        var whole = await ResolveAsync(AnalysisScope.Folder(bed.Library.Folder.Id));
        var onlyTrip = await ResolveAsync(AnalysisScope.Folder(bed.Library.Folder.Id, "trip"));

        Assert.Equal(new HashSet<Id<MediaItem>> { inAlbum.Id, other.Id }, whole.ItemIds.ToHashSet());
        Assert.Equal([other.Id], onlyTrip.ItemIds);
    }

    [Fact]
    public async Task Items_CoversTheChosenItemsOnce_AndIgnoresUnknownIds()
    {
        var chosen = await AddAsync(1, MediaItemStatus.Indexed);
        await AddAsync(2, MediaItemStatus.Indexed);

        var items = await ResolveAsync(AnalysisScope.Items([chosen.Id, chosen.Id, Id<MediaItem>.New()]));

        Assert.Equal([chosen.Id], items.ItemIds);
    }

    [Fact]
    public async Task SearchResults_CoverTheItemsOfTheCurrentView()
    {
        var first = await AddAsync(1, MediaItemStatus.Indexed);
        await AddAsync(2, MediaItemStatus.Indexed);
        view.Show(first.Id);

        var items = await ResolveAsync(AnalysisScope.SearchResults);

        Assert.Equal([first.Id], items.ItemIds);
    }

    [Fact]
    public async Task Resolve_WithoutAnOpenLibrary_FindsNothing()
    {
        await bed.Session.SetCurrentAsync(null);

        var items = await ResolveAsync(AnalysisScope.Pending);

        Assert.Equal(0, items.Count);
    }

    [Fact]
    public async Task AnalyzeScope_QueuesTheResolvedItemsWithTheAnalyzerSubset()
    {
        var pending = await AddAsync(1, MediaItemStatus.Pending);
        await AddAsync(2, MediaItemStatus.Indexed);
        var sender = new ScopeSender(bed, view);

        var result = await sender.Send(new AnalyzeScopeCommand(AnalysisScope.Pending, ["ocr"]), Token);

        Assert.Equal(1, result.ItemCount);
        Assert.NotNull(result.JobId);
        var queued = Assert.Single(sender.Queued);
        Assert.Equal([pending.Id], queued.ItemIds);
        Assert.Equal(["ocr"], queued.Analyzers);
        Assert.Equal("Analyze 1 pending items", queued.Description);
    }

    [Fact]
    public async Task AnalyzeScope_QueuesNothing_WhenTheScopeIsEmpty()
    {
        await AddAsync(1, MediaItemStatus.Indexed);
        var sender = new ScopeSender(bed, view);

        var result = await sender.Send(new AnalyzeScopeCommand(AnalysisScope.Pending), Token);

        Assert.Null(result.JobId);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(sender.Queued);
    }

    private async Task<MediaItem> AddAsync(int number, MediaItemStatus status, MediaType type = MediaType.Image, string? subfolder = null)
    {
        var item = MediaItem.Discover(
            bed.Library.Folder.Id,
            RelativePath.From($"{subfolder ?? "album"}/file_{number:D5}.{(type == MediaType.Video ? "mp4" : "jpg")}"),
            type,
            1000 + number,
            LibraryTestBed.Start,
            LibraryTestBed.Start,
            (UInt128)number * 7919,
            LibraryTestBed.Start);
        if (status != MediaItemStatus.Discovered)
        {
            item.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
            item.ChangeStatus(status, LibraryTestBed.Start);
        }

        await bed.Library.Library.Items.AddAsync(item, Token);
        return item;
    }

    private async Task<AnalysisScopeItems> ResolveAsync(AnalysisScope scope) =>
        await new ResolveAnalysisScopeQueryHandler(bed.Session, view).Handle(new ResolveAnalysisScopeQuery(scope), Token);

    private sealed class ViewSource(SearchTestBed bed) : IMediaResultSource
    {
        private IReadOnlyCollection<Id<MediaItem>>? shown;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public void Show(params Id<MediaItem>[] ids) => shown = ids;

        public Task<int> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<IReadOnlyList<MediaItemSummary>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken) =>
            shown is null
                ? await bed.Library.Library.Items.GetSummariesAsync(offset, limit, cancellationToken)
                : [.. (await bed.Library.Library.Items.GetSummariesByIdsAsync(shown, cancellationToken)).Skip(offset).Take(limit)];

        public Task<int> PositionOfAsync(Id<MediaItem> itemId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ScopeSender(SearchTestBed bed, ViewSource view) : ISender
    {
        public List<AnalyzeItemsCommand> Queued { get; } = [];

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object? response = request switch
            {
                ResolveAnalysisScopeQuery query => await new ResolveAnalysisScopeQueryHandler(bed.Session, view).Handle(query, cancellationToken),
                AnalyzeScopeCommand command => await new AnalyzeScopeCommandHandler(this).Handle(command, cancellationToken),
                AnalyzeItemsCommand command => Record(command),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return (TResponse)response!;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private Id<ProcessingJob>? Record(AnalyzeItemsCommand command)
        {
            Queued.Add(command);
            return Id<ProcessingJob>.New();
        }
    }
}
