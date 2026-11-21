using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// The library summary in the status bar (E02-S05-T04), with real library storage.
/// </summary>
public sealed class LibrarySummaryStatusSegmentTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build();
    private readonly ManualTimeProvider clock = new();
    private readonly List<IDisposable> disposables = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IOpenLibrary Library => app.Services.GetRequiredService<ILibrarySession>().Current!;

    [Fact]
    public async Task Segment_IsHiddenWithoutALibrary_AndShowsTheCountsOnceOneIsOpen()
    {
        var segment = NewSegment();
        Assert.False(segment.IsVisible);
        Assert.NotNull(segment.Command);

        await CreateLibraryAsync();
        await WaitUntilAsync(() =>
        {
            clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
            return segment.IsVisible && segment.Text!.StartsWith(Format.Number(3), StringComparison.Ordinal);
        });

        Assert.Equal($"{Format.Number(3)} in library · {Format.Number(2)} pending", segment.Text);
        Assert.Equal(
            $"{Format.Number(1)} indexed, {Format.Number(2)} pending, {Format.Number(0)} with errors, {Format.Number(0)} missing. Click for library information.",
            segment.ToolTip);

        await app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(null);
        await WaitUntilAsync(() => !segment.IsVisible);
    }

    [Fact]
    public async Task Segment_FollowsImportAndAnalysis_AndHidesThePendingPartAtZero()
    {
        var folder = await CreateLibraryAsync();
        var segment = NewSegment();
        await WaitUntilAsync(() => segment.IsVisible);

        // Import: new items arrive as Discovered.
        var added = Item(folder, "c.jpg", MediaType.Image);
        await Library.Items.AddAsync(added, Token);
        clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
        await WaitUntilAsync(() => segment.Text!.StartsWith(Format.Number(4), StringComparison.Ordinal));
        Assert.Equal($"{Format.Number(4)} in library · {Format.Number(3)} pending", segment.Text);

        // Analysis: every pending item ends up indexed.
        var items = await Library.Items.GetByFolderAsync(folder.Id, Token);
        foreach (var item in items.Where(item => item.Status is MediaItemStatus.Discovered))
        {
            item.ChangeStatus(MediaItemStatus.Pending, DateTimeOffset.UtcNow);
            item.ChangeStatus(MediaItemStatus.Indexed, DateTimeOffset.UtcNow);
            await Library.Items.UpdateAsync(item, Token);
        }

        clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
        await WaitUntilAsync(() => segment.Text == $"{Format.Number(4)} in library");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        await app.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition wasn't met in time.");
            await Task.Delay(10, Token);
        }
    }

    private static MediaItem Item(IndexedFolder folder, string path, MediaType type)
    {
        var now = DateTimeOffset.UtcNow;
        return MediaItem.Discover(folder.Id, RelativePath.From(path), type, 100, now, now, null, now);
    }

    private StatusBarSegment NewSegment()
    {
        var session = app.Services.GetRequiredService<ILibrarySession>();
        var monitor = new LibraryStatisticsMonitor(app.Services.GetRequiredService<ISender>(), session, clock, action => action());
        var viewModel = new LibraryInfoViewModel(monitor, session, TestLocalizer.EnUs(), app.Services.GetRequiredService<ISender>(), clock, action => action());
        disposables.Add(monitor);
        disposables.Add(viewModel);
        var localizer = TestLocalizer.EnUs();
        var statusBar = new StatusBarViewModel([new LibrarySummaryStatusSegment(monitor, new LibraryInfoPresenter(viewModel), localizer)]);
        return Assert.Single(statusBar.Right, segment => segment.Id == LibrarySummaryStatusSegment.SegmentId);
    }

    private async Task<IndexedFolder> CreateLibraryAsync()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var folder = IndexedFolder.Create(Library.Library.Id, Path.Combine(app.Root, "photos"), Path.Combine(app.Root, "photos"), DateTimeOffset.UtcNow);
        await Library.Folders.AddAsync(folder, Token);
        var indexed = Item(folder, "a.jpg", MediaType.Image);
        indexed.ChangeStatus(MediaItemStatus.Pending, DateTimeOffset.UtcNow);
        indexed.ChangeStatus(MediaItemStatus.Indexed, DateTimeOffset.UtcNow);
        var excluded = Item(folder, "x.jpg", MediaType.Image);
        excluded.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow);
        await Library.Items.AddRangeAsync([indexed, Item(folder, "b.jpg", MediaType.Image), Item(folder, "v.mp4", MediaType.Video), excluded], Token);
        return folder;
    }
}
