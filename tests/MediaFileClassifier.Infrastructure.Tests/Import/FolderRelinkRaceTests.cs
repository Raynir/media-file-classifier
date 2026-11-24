using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// Two availability checks that both find a folder's drive under another letter (MFC-1044): the one whose relink is
/// refused leaves the folder as the other one stored it, Online at its new root.
/// </summary>
public sealed class FolderRelinkRaceTests : IAsyncLifetime
{
    private const string Serial = "1A2B-3C4D";

    private readonly TemporaryFolder temp = new();
    private readonly GatedVolumeLocator volumes = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string OldRoot => temp.Combine("E");

    private string NewRoot => temp.Combine("F");

    public async ValueTask InitializeAsync() =>
        bed = await ImportTestBed.CreateAsync(root: OldRoot, configure: services => services.AddSingleton<IVolumeLocator>(volumes));

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task TwoChecks_ThatBothRelinkTheFolder_LeaveItOnlineAtTheNewRoot()
    {
        var folder = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        folder.ChangeVolumeId(Serial);
        await bed.Library.Folders.UpdateAvailabilityAsync(folder, Token);
        Directory.Move(OldRoot, NewRoot);
        volumes.Moved = new FolderHandle(NewRoot, NewRoot);

        await Task.WhenAll(
            bed.Sender.Send(new CheckFolderAvailabilityCommand(), Token),
            bed.Sender.Send(new CheckFolderAvailabilityCommand(), Token));

        var stored = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        Assert.Equal(2, volumes.Searches);
        Assert.Equal((NewRoot, FolderStatus.Online), (stored.PlatformHandle, stored.Status));
    }

    /// <summary>
    /// Finds the folder's drive under its new root only once both checks have looked, so both relink the same folder.
    /// </summary>
    private sealed class GatedVolumeLocator : IVolumeLocator
    {
        private readonly TaskCompletionSource bothSearched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int searches;

        public FolderHandle? Moved { get; set; }

        public int Searches => searches;

        public Task<string?> GetVolumeIdAsync(FolderHandle folder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Serial);

        public async Task<FolderHandle?> FindOnVolumeAsync(FolderHandle folder, string volumeId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref searches) == 2)
            {
                bothSearched.TrySetResult();
            }

            await bothSearched.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return Moved;
        }
    }
}
