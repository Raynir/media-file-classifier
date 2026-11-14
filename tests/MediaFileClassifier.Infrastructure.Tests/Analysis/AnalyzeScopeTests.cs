using System.Reflection;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
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
    public async Task Pending_LeavesOutItemsThatLeftPendingDuringTheRead()
    {
        var items = await AddManyAsync(2500, MediaItemStatus.Pending);

        var resolved = await ResolveWhileAsync(AnalysisScope.Pending, async () => await ChangeStatusAsync(items.Take(100).ToList(), MediaItemStatus.Indexed));

        Assert.Equal(items.Skip(100).Select(item => item.Id).ToHashSet(), resolved.ItemIds.ToHashSet());
        Assert.Equal(2400, resolved.Count);
    }

    [Fact]
    public async Task Folder_StillFindsEveryItemOfTheStart_WhenTheyLeavePendingDuringTheRead()
    {
        var items = await AddManyAsync(2500, MediaItemStatus.Pending);

        var resolved = await ResolveWhileAsync(AnalysisScope.Folder(bed.Library.Folder.Id), async () => await ChangeStatusAsync(items, MediaItemStatus.Indexed));

        Assert.Equal(items.Select(item => item.Id).ToHashSet(), resolved.ItemIds.ToHashSet());
    }

    [Fact]
    public async Task SearchResults_StillFindEveryItemOfTheView_WhenTheirStatusChangesBetweenPages()
    {
        var items = await AddManyAsync(2500, MediaItemStatus.Pending);
        view.Show([.. items.Select(item => item.Id)]);
        view.AfterIdPage = async () => await ChangeStatusAsync(items, MediaItemStatus.Indexed);

        var resolved = await ResolveAsync(AnalysisScope.SearchResults);

        Assert.Equal(items.Select(item => item.Id).ToHashSet(), resolved.ItemIds.ToHashSet());
        Assert.Equal(items.Count, resolved.ItemIds.Distinct().Count());
    }

    [Fact]
    public async Task SearchResults_WithoutASearch_StillFindEveryRemainingItem_WhenItemsAreDeletedDuringTheRead()
    {
        var items = await AddManyAsync(2500, MediaItemStatus.Pending);
        var deleted = items.OrderByDescending(item => item.Id.Value).Take(100).Select(item => item.Id).ToList();
        view.AfterIdPage = async () =>
        {
            view.AfterIdPage = null;
            foreach (var id in deleted)
            {
                await bed.Library.Library.Items.DeleteAsync(id, Token);
            }
        };

        var resolved = await ResolveAsync(AnalysisScope.SearchResults);

        Assert.Equal(items.Select(item => item.Id).Except(deleted).ToHashSet(), resolved.ItemIds.ToHashSet());
    }

    [Fact]
    public async Task SearchResults_ReadInTheOrderOfTheView_AndLeaveOutMissingItems()
    {
        var first = await AddAsync(1, MediaItemStatus.Indexed);
        var missing = await AddAsync(2, MediaItemStatus.Missing);
        var third = await AddAsync(3, MediaItemStatus.Pending);
        view.Show(third.Id, missing.Id, first.Id);

        var resolved = await ResolveAsync(AnalysisScope.SearchResults);

        Assert.Equal([third.Id, first.Id], resolved.ItemIds);
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

    private async Task<List<MediaItem>> AddManyAsync(int count, MediaItemStatus status)
    {
        var items = new List<MediaItem>(count);
        for (var number = 1; number <= count; number++)
        {
            items.Add(await AddAsync(number, status));
        }

        return items;
    }

    private async Task ChangeStatusAsync(List<MediaItem> items, MediaItemStatus status)
    {
        foreach (var item in items)
        {
            item.ChangeStatus(status, LibraryTestBed.Start);
        }

        await bed.Library.Library.Items.UpdateRangeAsync(items, Token);
    }

    // Resolves a scope, running the change right after the repository answered with the items of the scope: the
    // moment where an item's status can change between "in the scope" and "read".
    private async Task<AnalysisScopeItems> ResolveWhileAsync(AnalysisScope scope, Func<Task> change)
    {
        var session = new HookedSession(bed.Library.Library, change);
        return await new ResolveAnalysisScopeQueryHandler(session, view).Handle(new ResolveAnalysisScopeQuery(scope), Token);
    }

    private sealed class HookedSession(IOpenLibrary inner, Func<Task> afterScopeRead) : ILibrarySession
    {
        public event EventHandler? CurrentChanged
        {
            add { }
            remove { }
        }

        public IOpenLibrary? Current { get; } = HookedLibrary.Create(inner, afterScopeRead);

        public Task SetCurrentAsync(IOpenLibrary? library) => throw new NotSupportedException();
    }

    // Forwards everything to the real library; its item repository runs a callback after answering a read of ids.
    private class HookedLibrary : DispatchProxy
    {
        private IOpenLibrary inner = null!;
        private IMediaItemRepository items = null!;

        public static IOpenLibrary Create(IOpenLibrary inner, Func<Task> afterScopeRead)
        {
            var repository = DispatchProxy.Create<IMediaItemRepository, HookedRepository>();
            ((HookedRepository)repository).Set(inner.Items, afterScopeRead);
            var library = DispatchProxy.Create<IOpenLibrary, HookedLibrary>();
            ((HookedLibrary)(object)library).inner = inner;
            ((HookedLibrary)(object)library).items = repository;
            return library;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == nameof(IOpenLibrary.Items) || targetMethod.Name == "get_" + nameof(IOpenLibrary.Items)
                ? items
                : targetMethod.Invoke(inner, args);
    }

    private class HookedRepository : DispatchProxy
    {
        private IMediaItemRepository inner = null!;
        private Func<Task> afterScopeRead = null!;

        public void Set(IMediaItemRepository repository, Func<Task> callback)
        {
            inner = repository;
            afterScopeRead = callback;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(inner, args);
            return targetMethod.Name == nameof(IMediaItemRepository.GetFilteredIdsAsync)
                ? ThenAsync((Task<IReadOnlyList<Id<MediaItem>>>)result!)
                : result;
        }

        private async Task<IReadOnlyList<Id<MediaItem>>> ThenAsync(Task<IReadOnlyList<Id<MediaItem>>> read)
        {
            var ids = await read;
            await afterScopeRead();
            return ids;
        }
    }

    private sealed class ViewSource(SearchTestBed bed) : IMediaResultSource
    {
        private IReadOnlyCollection<Id<MediaItem>>? shown;

        public Func<Task>? AfterIdPage { get; set; }

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

        public async Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken)
        {
            // A fixed list, in the order shown, as the search keeps its hits for the length of a search.
            var page = shown is null
                ? await bed.Library.Library.Items.GetIdsAsync(offset, limit, newest, cancellationToken)
                : [.. shown.Skip(offset).Take(limit)];
            if (AfterIdPage is { } after)
            {
                await after();
            }

            return page;
        }

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
