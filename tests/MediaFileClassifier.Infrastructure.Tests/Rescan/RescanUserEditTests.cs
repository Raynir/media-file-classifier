using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// A user edit made while a rescan stage holds an item it read is kept (MFC-1053): the stage writes only what it found
/// out about the file.
/// </summary>
public sealed class RescanUserEditTests : IAsyncLifetime
{
    private readonly PausingFileHasher hasher = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync(configure: services => services.AddSingleton<IFileHasher>(hasher));
        bed.Copy(SampleMedia.JpegWithExif, "photo.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task FavoriteSetWhileTheCheckStageRuns_IsKept()
    {
        var item = Assert.Single(await bed.ItemsAsync());
        File.SetLastWriteTimeUtc(Path.Combine(bed.Root, "photo.jpg"), DateTime.UtcNow.AddMinutes(-5));
        hasher.Pause();

        await bed.Sender.Send(new StartRescanCommand(bed.Folder.Id), Token);
        Assert.True(await hasher.Paused.WaitAsync(TimeSpan.FromSeconds(30), Token));
        await bed.Library.Items.SetFavoriteAsync([item.Id], true, DateTimeOffset.UtcNow, Token);
        hasher.Resume();
        await bed.WaitUntilIdleAsync();

        var stored = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.True(stored.IsFavorite);
        Assert.NotEqual(item.FileModifiedAt, stored.FileModifiedAt);
    }

    // Holds the first fingerprint after Pause until Resume, so the check stage stops with the item it read.
    private sealed class PausingFileHasher : IFileHasher
    {
        private readonly XxHashFileHasher inner = new();
        private readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;

        public SemaphoreSlim Paused { get; } = new(0);

        public void Pause() => armed = 1;

        public void Resume() => resumed.TrySetResult();

        public async Task<UInt128> ComputeFingerprintAsync(Stream content, long length, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                Paused.Release();
                await resumed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.ComputeFingerprintAsync(content, length, cancellationToken).ConfigureAwait(false);
        }

        public Task<UInt128> ComputeContentHashAsync(Stream content, CancellationToken cancellationToken = default) =>
            inner.ComputeContentHashAsync(content, cancellationToken);
    }
}
