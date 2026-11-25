using MediaFileClassifier.UseCases.Import;

namespace MediaFileClassifier.UseCases.Tests.Import;

/// <summary>
/// The gate that keeps a rescan's reconcile apart from imports registering files (MFC-1040).
/// </summary>
public sealed class FolderScanGateTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    private readonly FolderScanGate gate = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shared_HoldsAtTheSameTime()
    {
        using var first = await gate.EnterSharedAsync(Token);
        var second = gate.EnterSharedAsync(Token);

        Assert.True(second.IsCompletedSuccessfully);
        (await second).Dispose();
    }

    [Fact]
    public async Task Exclusive_WaitsForEverySharedHold()
    {
        var first = await gate.EnterSharedAsync(Token);
        var second = await gate.EnterSharedAsync(Token);
        var exclusive = gate.EnterExclusiveAsync(Token);

        first.Dispose();
        await Task.Delay(Short, Token);
        Assert.False(exclusive.IsCompleted);

        second.Dispose();
        (await exclusive.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task Shared_WaitsForTheExclusiveHold()
    {
        var exclusive = await gate.EnterExclusiveAsync(Token);
        var shared = gate.EnterSharedAsync(Token);

        await Task.Delay(Short, Token);
        Assert.False(shared.IsCompleted);

        exclusive.Dispose();
        (await shared.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task WaitingExclusive_GoesBeforeLaterSharedHolds()
    {
        var first = await gate.EnterSharedAsync(Token);
        var exclusive = gate.EnterExclusiveAsync(Token);
        var later = gate.EnterSharedAsync(Token);

        await Task.Delay(Short, Token);
        Assert.False(later.IsCompleted);

        first.Dispose();
        var hold = await exclusive.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.False(later.IsCompleted);

        hold.Dispose();
        (await later.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task CancelledWaits_LeaveTheGateUsable()
    {
        var exclusive = await gate.EnterExclusiveAsync(Token);
        using var cancel = new CancellationTokenSource();
        var shared = gate.EnterSharedAsync(cancel.Token);
        var other = gate.EnterExclusiveAsync(cancel.Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shared);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);

        exclusive.Dispose();
        (await gate.EnterSharedAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
        (await gate.EnterExclusiveAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }

    [Fact]
    public async Task DisposingAHoldTwice_ReleasesOnce()
    {
        var first = await gate.EnterSharedAsync(Token);
        var second = await gate.EnterSharedAsync(Token);
        var exclusive = gate.EnterExclusiveAsync(Token);

        first.Dispose();
        first.Dispose();
        await Task.Delay(Short, Token);

        Assert.False(exclusive.IsCompleted);
        second.Dispose();
        (await exclusive.WaitAsync(TimeSpan.FromSeconds(5), Token)).Dispose();
    }
}
