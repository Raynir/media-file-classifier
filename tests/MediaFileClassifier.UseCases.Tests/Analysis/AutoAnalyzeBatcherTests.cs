using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.AutoAnalyze;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AutoAnalyzeBatcher"/> and <see cref="AutoAnalyzeHandler"/>: automatic analysis of imported items
/// (E06-S06-T01).
/// </summary>
public sealed class AutoAnalyzeBatcherTests : IDisposable
{
    private readonly InMemorySettingsStore settings = new() { IsLibraryOpen = true };
    private readonly FakeLibrarySession session = new();
    private readonly RecordingSender sender = new();
    private readonly AutoAnalyzeBatcher batcher;

    public AutoAnalyzeBatcherTests()
    {
        batcher = new AutoAnalyzeBatcher(settings, session, sender, TimeProvider.System, NullLogger<AutoAnalyzeBatcher>.Instance);
    }

    public void Dispose() => batcher.Dispose();

    [Fact]
    public async Task Flush_QueuesTheCollectedItemsAsOneJob()
    {
        await OpenLibraryAsync();
        var items = Enumerable.Range(0, 3).Select(_ => Id<MediaItem>.New()).ToList();

        foreach (var item in items)
        {
            batcher.Add(item);
        }

        await batcher.FlushAsync();

        var command = Assert.Single(sender.Commands);
        Assert.Equal(items, command.ItemIds);
        Assert.Null(command.Analyzers);
        Assert.Equal(0, batcher.Waiting);
    }

    [Fact]
    public async Task Handler_PassesTheRegisteredItemOn()
    {
        await OpenLibraryAsync();
        var item = Id<MediaItem>.New();

        await new AutoAnalyzeHandler(batcher).Handle(new MediaItemRegistered(item, Id<IndexedFolder>.New()), CancellationToken.None);
        await batcher.FlushAsync();

        Assert.Equal([item], Assert.Single(sender.Commands).ItemIds);
    }

    [Fact]
    public async Task Add_QueuesAtOnce_WhenTheBatchIsFull()
    {
        await OpenLibraryAsync();

        for (var count = 0; count < AutoAnalyzeBatcher.MaxBatch; count++)
        {
            batcher.Add(Id<MediaItem>.New());
        }

        await sender.Queued.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(AutoAnalyzeBatcher.MaxBatch, Assert.Single(sender.Commands).ItemIds.Count);
        Assert.Equal(0, batcher.Waiting);
    }

    [Fact]
    public async Task Add_QueuesNothing_WhenAutoAnalyzeIsOff()
    {
        await OpenLibraryAsync();
        settings.SetValue(KnownSettings.AnalyzeAutomatically, false);

        batcher.Add(Id<MediaItem>.New());
        await batcher.FlushAsync();

        Assert.Empty(sender.Commands);
        Assert.Equal(0, batcher.Waiting);
    }

    [Fact]
    public async Task Flush_QueuesNothing_WhenTheSettingWasSwitchedOffWhileCollecting()
    {
        await OpenLibraryAsync();
        batcher.Add(Id<MediaItem>.New());

        settings.SetValue(KnownSettings.AnalyzeAutomatically, false);
        await batcher.FlushAsync();

        Assert.Empty(sender.Commands);
    }

    [Fact]
    public void Add_QueuesNothing_WithoutAnOpenLibrary()
    {
        batcher.Add(Id<MediaItem>.New());

        Assert.Equal(0, batcher.Waiting);
        Assert.Empty(sender.Commands);
    }

    [Fact]
    public async Task Flush_DropsItemsOfALibraryThatClosed()
    {
        await OpenLibraryAsync();
        batcher.Add(Id<MediaItem>.New());

        await session.SetCurrentAsync(null);
        await batcher.FlushAsync();

        Assert.Empty(sender.Commands);
        Assert.Equal(0, batcher.Waiting);
    }

    [Fact]
    public async Task Flush_KeepsWorking_WhenQueueingFails()
    {
        await OpenLibraryAsync();
        sender.Failure = new InvalidOperationException("no scheduler");
        batcher.Add(Id<MediaItem>.New());

        await batcher.FlushAsync();

        sender.Failure = null;
        batcher.Add(Id<MediaItem>.New());
        await batcher.FlushAsync();
        Assert.Single(sender.Commands);
    }

    private Task OpenLibraryAsync() => session.SetCurrentAsync(new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "auto"), "auto"));

    private sealed class RecordingSender : FakeSender
    {
        public List<AnalyzeItemsCommand> Commands { get; } = [];

        public TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; set; }

        protected override object? Handle(object request)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Commands.Add((AnalyzeItemsCommand)request);
            Queued.TrySetResult();
            return null;
        }
    }
}
