using Xunit.Sdk;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// The test bed's wait for an idle queue (MFC-1069): a queue that is still draining isn't taken for a stuck one, and a
/// stuck one still fails the wait. The pending counts are scripted per poll, so the checks don't depend on the machine's
/// speed.
/// </summary>
public sealed class ImportTestBedTests
{
    [Fact]
    public async Task WaitUntilDrainedAsync_CountGrowsThenDrainsLongerThanTheStallTimeout_Returns()
    {
        // As an import: only discovery is pending at first, then it queues the files, which drain one by one. The whole
        // drain (at least 200 polls of 20 ms) takes longer than the stall timeout, but the count changes every 2 polls.
        var counts = new Queue<int>([1, .. Enumerable.Range(1, 100).Reverse().SelectMany(count => new[] { count, count }), 0]);

        await ImportTestBed.WaitUntilDrainedAsync(_ => Task.FromResult(counts.Dequeue()), TimeSpan.FromSeconds(2));

        Assert.Empty(counts);
    }

    [Fact]
    public async Task WaitUntilDrainedAsync_CountNeverChanges_FailsAfterTheStallTimeout()
    {
        var exception = await Assert.ThrowsAnyAsync<XunitException>(
            () => ImportTestBed.WaitUntilDrainedAsync(_ => Task.FromResult(5), TimeSpan.FromMilliseconds(200)));

        Assert.Contains("5 work items still pending", exception.Message, StringComparison.Ordinal);
    }
}
