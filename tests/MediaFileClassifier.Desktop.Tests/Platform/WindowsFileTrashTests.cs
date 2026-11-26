using MediaFileClassifier.Desktop.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Testing.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Desktop.Tests.Platform;

/// <summary>
/// Tests of <see cref="WindowsFileTrash"/> and <see cref="ShellRecycler"/> against the real Windows shell. Every file is a
/// new one in a <see cref="TemporaryFolder"/>; a test that moves one leaves those few bytes in this machine's Recycle Bin.
/// </summary>
public sealed class WindowsFileTrashTests : IDisposable
{
    private readonly TemporaryFolder folder = new();
    private readonly WindowsFileTrash trash = new(NullLogger<WindowsFileTrash>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MoveToTrash_LocalFile_GoesToTheRecycleBin()
    {
        var path = NewFile("photo.jpg");

        var results = await trash.MoveToTrashAsync([path], Token);

        Assert.Equal([TrashResult.Moved(path)], results);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task MoveToTrash_ReadOnlyFile_IsSkippedAndStays()
    {
        var path = NewFile("locked.jpg");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var results = await trash.MoveToTrashAsync([path], Token);

        Assert.Equal([TrashResult.Skipped(path, TrashSkipReason.ReadOnly)], results);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task MoveToTrash_FileOnANetworkShare_IsRefusedAndStays()
    {
        var path = NewFile("shared.jpg");
        var sharePath = AdminSharePath(path);

        var results = await trash.MoveToTrashAsync([sharePath], Token);

        Assert.Equal([TrashResult.Skipped(sharePath, TrashSkipReason.NoTrash)], results);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Recycle_FileTheShellCanOnlyDeletePermanently_IsRefusedAndStays()
    {
        // Past the first guard: the shell itself is asked to recycle a file on a share, which has no Recycle Bin.
        var path = NewFile("shared.jpg");
        var sharePath = AdminSharePath(path);
        Assert.SkipUnless(File.Exists(sharePath), $"The administrative share {sharePath} isn't reachable here.");

        var (outcome, _) = await RunOnStaAsync(() => ShellRecycler.Recycle(sharePath));

        Assert.Equal(ShellRecycleOutcome.Refused, outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task MoveToTrash_PathsThatAreNoFile_AreSkippedWithTheReason()
    {
        var missing = folder.Combine("missing.jpg");
        var subfolder = Directory.CreateDirectory(folder.Combine("sub")).FullName;

        var results = await trash.MoveToTrashAsync([missing, subfolder, "relative.jpg", string.Empty], Token);

        Assert.Equal(
            [
                TrashResult.Skipped(missing, TrashSkipReason.NotFound),
                TrashResult.Skipped(subfolder, TrashSkipReason.NotAFile),
                TrashResult.Skipped("relative.jpg", TrashSkipReason.NotAFile),
                TrashResult.Skipped(string.Empty, TrashSkipReason.NotAFile),
            ],
            results);
        Assert.True(Directory.Exists(subfolder));
    }

    [Fact]
    public async Task MoveToTrash_SeveralFiles_ReportsEachInTheOrderGiven()
    {
        var first = NewFile("a.jpg");
        var missing = folder.Combine("gone.jpg");
        var last = NewFile("b.jpg");

        var results = await trash.MoveToTrashAsync([first, missing, last], Token);

        Assert.Equal(
            [TrashResult.Moved(first), TrashResult.Skipped(missing, TrashSkipReason.NotFound), TrashResult.Moved(last)],
            results);
    }

    [Fact]
    public async Task MoveToTrash_Cancelled_ReportsEveryPathAsCancelledAndMovesNothing()
    {
        var path = NewFile("kept.jpg");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var results = await trash.MoveToTrashAsync([path], cancellation.Token);

        Assert.Equal([TrashResult.Skipped(path, TrashSkipReason.Cancelled)], results);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(0x80u, true)]
    [InlineData(0x80u | 0x10u, true)]
    [InlineData(0x0u, false)]
    [InlineData(0x10u | 0x40u, false)]
    public void WillRecycle_TransferFlags_AllowOnlyARecyclingDelete(uint flags, bool expected) =>
        Assert.Equal(expected, ShellRecycler.WillRecycle(flags));

    [Fact]
    public void Recycle_OffAnStaThread_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ShellRecycler.Recycle(folder.Combine("any.jpg")));

    public void Dispose()
    {
        // A read-only file would keep the folder from being deleted.
        foreach (var file in Directory.EnumerateFiles(folder.Path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        folder.Dispose();
    }

    // The same file seen through the drive's administrative share, e.g. \\localhost\C$\Users\...: a network path.
    private static string AdminSharePath(string path) => $@"\\localhost\{path[0]}${path[2..]}";

    private static Task<T> RunOnStaAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private string NewFile(string name)
    {
        var path = folder.Combine(name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
