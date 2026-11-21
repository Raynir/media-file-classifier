using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediaFileClassifier.UseCases.Libraries.GetLibraryDiskUsage;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// The library information view and its live statistics (E02-S05-T02), with real library storage.
/// </summary>
public sealed class LibraryInfoTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build();
    private readonly ManualTimeProvider clock = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IOpenLibrary Library => app.Services.GetRequiredService<ILibrarySession>().Current!;

    [Fact]
    public async Task Monitor_CountsTheOpenLibrary_AndCountsAgainOnceAfterABurstOfChanges()
    {
        var folder = await CreateLibraryAsync();
        using var monitor = NewMonitor(out var sender);
        monitor.Start();
        await WaitUntilAsync(() => monitor.Current is not null);
        Assert.Equal(new LibraryStatistics(2, 0, 1, 1, 2, 0, 0, 300), monitor.Current);
        var queries = sender.Count;

        // An import writes in batches; the counts wait for the interval, then cover all of them in one query.
        await Library.Items.AddAsync(Item(folder, "c.jpg", MediaType.Image), Token);
        await Library.Items.AddAsync(Item(folder, "d.mp4", MediaType.Video), Token);
        Assert.Equal(3, monitor.Current!.Total);
        clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
        await WaitUntilAsync(() => monitor.Current!.Total == 5);

        Assert.Equal(queries + 1, sender.Count);
        Assert.Equal(new LibraryStatistics(3, 0, 2, 1, 4, 0, 0, 500), monitor.Current);
    }

    [Fact]
    public async Task Monitor_ForgetsTheStatistics_WhenTheLibraryCloses()
    {
        await CreateLibraryAsync();
        using var monitor = NewMonitor(out _);
        monitor.Start();
        await WaitUntilAsync(() => monitor.Current is not null);
        var changes = 0;
        monitor.Changed += (_, _) => changes++;

        await app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(null);
        await WaitUntilAsync(() => monitor.Current is null);

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Monitor_ReportsAChange_WhenAnotherLibraryWithTheSameCountsOpens()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("First", app.Root), Token);
        using var monitor = NewMonitor(out _);
        monitor.Start();
        await WaitUntilAsync(() => monitor.Current is not null);
        var changes = 0;
        monitor.Changed += (_, _) => changes++;

        // Both libraries are empty, so the counts are equal; views still need to know to reload.
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Second", Path.Combine(app.Root, "second")), Token);
        await WaitUntilAsync(() => changes > 0);
    }

    [Fact]
    public async Task ViewModel_ShowsTheGroupsAndRows_AndRefreshesWhileItemsAreImported()
    {
        var folder = await CreateLibraryAsync();
        using var monitor = NewMonitor(out _);
        using var viewModel = new LibraryInfoViewModel(monitor, app.Services.GetRequiredService<ILibrarySession>(), TestLocalizer.EnUs(), app.Services.GetRequiredService<ISender>(), clock, action => action());
        Assert.False(viewModel.HasLibrary);

        viewModel.Attach();
        await WaitUntilAsync(() => viewModel.HasLibrary);

        Assert.Equal("Family", viewModel.LibraryName);
        Assert.Equal(["Items", "Status", "Disk usage"], viewModel.Groups.Select(group => group.Header));
        Assert.Equal(
            ["Total: 3", "Images: 2", "Animated images: 0", "Videos: 1", "Indexed: 1", "Pending: 2", "Errors: 0", "Missing: 0", $"Media files: {Format.FileSize(300)}"],
            viewModel.Groups.SelectMany(group => group.Rows).Take(9).Select(row => row.AccessibleName));

        // The disk usage is measured in the background, and matches the library's files.
        var disk = viewModel.Groups[2].Rows;
        await WaitUntilAsync(() => disk[1].Value != "Calculating…");
        var actual = LibraryDiskUsageMeter.Measure(Library.Layout);
        Assert.True(actual.DatabaseBytes > 0);
        Assert.Equal(
            [Format.FileSize(actual.DatabaseBytes), Format.FileSize(actual.ThumbnailsBytes), Format.FileSize(actual.BackupsBytes), Format.FileSize(actual.TotalBytes)],
            disk.Skip(1).Select(row => row.Value));

        await Library.Items.AddAsync(Item(folder, "e.jpg", MediaType.Image), Token);
        clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
        await WaitUntilAsync(() => viewModel.Groups[0].Rows[0].Value == "4");

        // Hidden, it stops following: a later change leaves the rows as they were.
        viewModel.Detach();
        await Library.Items.AddAsync(Item(folder, "f.jpg", MediaType.Image), Token);
        clock.Advance(LibraryStatisticsMonitor.RefreshInterval);
        await WaitUntilAsync(() => monitor.Current!.Total == 5);
        Assert.Equal("4", viewModel.Groups[0].Rows[0].Value);
    }

    [Fact]
    public Task Window_ShowsTheRows_AndCloseClosesIt() => TestApp.RunAsync(() =>
    {
        var sender = new StatisticsSender();
        var session = new FakeBrowserSession();
        var monitor = new LibraryStatisticsMonitor(sender, session, clock, action => action());
        using var viewModel = new LibraryInfoViewModel(monitor, session, Localizer.Current, sender, clock, action => action());
        viewModel.Attach();
        Dispatcher.UIThread.RunJobs();

        // Disk usage loading: the item rows show at once, the disk rows say so until the sizes are in.
        var disk = viewModel.Groups[2].Rows;
        Assert.All(disk.Skip(1), row => Assert.Equal(Localizer.Current.Get("LibraryInfo_Calculating"), row.Value));
        sender.Disk.SetResult(new LibraryDiskUsage(1024, 2048, 0));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Format.FileSize(3072), disk[^1].Value);

        // Every 30 seconds while shown, it's measured again (the cache answers when it's younger).
        clock.Advance(LibraryInfoViewModel.DiskUsageInterval);
        Assert.Equal(2, sender.DiskQueries);
        var window = new LibraryInfoWindow { DataContext = viewModel };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.HasLibrary);
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
        Assert.True(texts.Contains("Animated images"), string.Join("|", texts));
        Assert.Contains(Format.Number(12_345), texts);
        var close = window.FindControl<Button>("CloseButton")!;
        Assert.True(close.IsCancel);
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        monitor.Dispose();
    });

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private static MediaItem Item(IndexedFolder folder, string path, MediaType type)
    {
        var now = DateTimeOffset.UtcNow;
        return MediaItem.Discover(folder.Id, RelativePath.From(path), type, 100, now, now, null, now);
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

    private LibraryStatisticsMonitor NewMonitor(out CountingSender sender)
    {
        sender = new CountingSender(app.Services.GetRequiredService<ISender>());
        return new LibraryStatisticsMonitor(sender, app.Services.GetRequiredService<ILibrarySession>(), clock, action => action());
    }

    // Two images (one indexed) and a video, plus an excluded image that doesn't count.
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

    /// <summary>
    /// Passes requests on and counts the statistics queries.
    /// </summary>
    private sealed class CountingSender(ISender inner) : FakeSender
    {
        public int Count { get; private set; }

        public override Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetLibraryStatisticsQuery)
            {
                Count++;
            }

            return inner.Send(request, cancellationToken);
        }
    }

    /// <summary>
    /// Answers fixed statistics.
    /// </summary>
    private sealed class StatisticsSender : FakeSender
    {
        public TaskCompletionSource<LibraryDiskUsage?> Disk { get; } = new();

        public int DiskQueries { get; private set; }

        public override Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetLibraryDiskUsageQuery)
            {
                DiskQueries++;
                return (Task<TResponse>)(object)Disk.Task;
            }

            return base.Send(request, cancellationToken);
        }

        protected override object? Handle(object request) => request switch
        {
            GetLibraryStatisticsQuery => new LibraryStatistics(12_000, 345, 0, 12_345, 0, 0, 0, 1_000_000),
            _ => throw new NotSupportedException(),
        };
    }
}
