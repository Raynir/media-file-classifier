using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// End-to-end test of live folder status (E14-S07-T04): reconnecting a drive brings its items back to normal display
/// on its own, through the volume event, the debounced check and the results' change notice, with a real library.
/// </summary>
public sealed class LiveFolderStatusEndToEndTests : IAsyncLifetime
{
    private readonly TemporaryFolder temp = new();
    private readonly FakeVolumeWatcher volumes = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Drive => temp.Combine("drive");

    public async ValueTask InitializeAsync() =>
        bed = await ImportTestBed.CreateAsync(root: Drive, configure: services => services.AddSingleton<IVolumeWatcher>(volumes));

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task ReconnectingTheDrive_RestoresItsItems_WithoutAnyUserAction()
    {
        bed.WriteDistinctPhotos(2);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var source = bed.Get<LibraryItemsSource>();
        var unplugged = Drive + "-unplugged";
        Directory.Move(Drive, unplugged);
        await bed.Sender.Send(new CheckFolderAvailabilityCommand(), Token);
        Assert.All(await source.GetPageAsync(0, 10, Token), item => Assert.True(item.IsFolderOffline));

        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Changed += (_, _) => refreshed.TrySetResult();
        using var monitor = bed.Get<FolderAvailabilityMonitor>();
        monitor.Start();

        Directory.Move(unplugged, Drive);
        volumes.Raise();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(FolderStatus.Online, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.Status);
        var items = await source.GetPageAsync(0, 10, Token);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.IsFolderOffline));
    }

    private sealed class FakeVolumeWatcher : IVolumeWatcher
    {
        public event EventHandler? VolumesChanged;

        public void Raise() => VolumesChanged?.Invoke(this, EventArgs.Empty);
    }
}
