using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Activity;

/// <summary>
/// Tests for <see cref="RecordActivityCommand"/> (E15-S04-T01): what other features call to add an event to the log.
/// </summary>
public sealed class RecordActivityCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Record_StoresTheEvent_WithItsCountsAndTime_AndTellsTheViews()
    {
        var (handler, library, changes) = await CreateAsync(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));
        var raised = 0;
        changes.Changed += (_, _) => raised++;

        await handler.Handle(
            new RecordActivityCommand(
                ActivityEventType.Import,
                ActivitySeverity.Warning,
                @"D:\Photos",
                new Dictionary<string, long> { [ActivityCounts.Items] = 12, [ActivityCounts.Failed] = 2 }),
            Token);

        var stored = Assert.Single(library.ActivityList.Events);
        Assert.Equal((ActivityEventType.Import, ActivitySeverity.Warning, @"D:\Photos"), (stored.Type, stored.Severity, stored.Subject));
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero), stored.OccurredAt);
        Assert.Equal((12L, 2L, 0L), (stored.Count(ActivityCounts.Items), stored.Count(ActivityCounts.Failed), stored.Count(ActivityCounts.Missing)));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Record_WithoutAnOpenLibrary_DoesNothing()
    {
        var changes = new ActivityChanges();
        var raised = 0;
        changes.Changed += (_, _) => raised++;
        var handler = new RecordActivityCommandHandler(new FakeLibrarySession(), TimeProvider.System, changes, NullLogger<RecordActivityCommandHandler>.Instance);

        await handler.Handle(new RecordActivityCommand(ActivityEventType.Rescan), Token);

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Record_WhenTheLogCantBeWritten_DoesNotFailTheCaller_AndTellsNoOne()
    {
        var (handler, library, changes) = await CreateAsync(DateTimeOffset.UnixEpoch);
        library.ActivityList.Fails = true;
        var raised = 0;
        changes.Changed += (_, _) => raised++;

        await handler.Handle(new RecordActivityCommand(ActivityEventType.BulkEdit), Token);

        Assert.Empty(library.ActivityList.Events);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Record_WhenCancelled_Throws()
    {
        var (handler, library, _) = await CreateAsync(DateTimeOffset.UnixEpoch);
        library.ActivityList.Cancels = true;

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.Handle(new RecordActivityCommand(ActivityEventType.BulkEdit), Token));
    }

    private static async Task<(RecordActivityCommandHandler Handler, FakeOpenLibrary Library, ActivityChanges Changes)> CreateAsync(DateTimeOffset now)
    {
        var session = new FakeLibrarySession();
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "x"), "x");
        await session.SetCurrentAsync(library);
        var changes = new ActivityChanges();
        var handler = new RecordActivityCommandHandler(session, new FixedClock(now), changes, NullLogger<RecordActivityCommandHandler>.Instance);
        return (handler, library, changes);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
