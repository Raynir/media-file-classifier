using System.Diagnostics;
using System.Security.Cryptography;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for the "Waiting for model" job state (D037, E06-S12-T03): work that needs a missing model isn't dispatched,
/// its job waits, and it runs once the model is installed, against a real library database and model store.
/// </summary>
public sealed class WaitingForModelTests : IAsyncLifetime
{
    private const string EmbedStage = "test.embed";
    private const string ThumbnailStage = "test.thumbnail";

    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] modelFile = RandomNumberGenerator.GetBytes(1024);
    private LibraryTestBed bed = null!;
    private ModelStore store = null!;
    private ModelCatalog catalog = null!;
    private ModelAvailability availability = null!;
    private JobScheduler? scheduler;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        store = new ModelStore(Path.Combine(root, "models"), TimeProvider.System);
        catalog = new ModelCatalog($$"""
            {
              "models": [
                {
                  "modelId": "embedder", "version": "1", "displayName": "Embedder", "purpose": "Embedding", "license": "MIT",
                  "licenseUrl": "https://example.com/license", "source": "https://example.com/embedder", "defaultTier": "standard",
                  "tiers": [
                    {
                      "tierId": "standard", "displayName": "Standard",
                      "files": [
                        { "role": "vision", "fileName": "vision.onnx", "url": "https://example.com/vision.onnx", "size": {{modelFile.Length}},
                          "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(modelFile))}}" }
                      ]
                    }
                  ]
                }
              ]
            }
            """);
        availability = new ModelAvailability(catalog, store);
    }

    public async ValueTask DisposeAsync()
    {
        if (scheduler is not null)
        {
            await scheduler.DisposeAsync();
        }

        availability.Dispose();
        store.Dispose();
        await bed.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NoModel_JobWaits_ThenAnalyzesEveryItemOnceTheModelIsInstalled()
    {
        var embed = new ModelStage(EmbedStage);
        Start(embed);

        var jobId = await scheduler!.EnqueueJobAsync(Job(Enumerable.Repeat(new WorkItemRequest(EmbedStage), 10)), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);
        await Task.Delay(100, Token);
        Assert.Empty(embed.Started);
        Assert.Equal(0, await bed.Library.Jobs.CountPendingAsync(Token));

        await InstallModelAsync();

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Equal(10, embed.Started.Count);
        Assert.All(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token), item => Assert.Equal(WorkItemState.Completed, item.State));
    }

    [Fact]
    public async Task MixedJob_RunsTheWorkThatNeedsNoModel_ThenWaits()
    {
        var thumbnails = new TestStageHandler(ThumbnailStage, ResourcePool.Cpu);
        var embed = new ModelStage(EmbedStage);
        Start(thumbnails, embed);

        var jobId = await scheduler!.EnqueueJobAsync(
            Job([.. Enumerable.Repeat(new WorkItemRequest(EmbedStage), 3), .. Enumerable.Repeat(new WorkItemRequest(ThumbnailStage), 5)]),
            Token);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);

        Assert.Equal(5, thumbnails.Started.Count);
        Assert.Empty(embed.Started);
        var items = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        Assert.All(items.Where(item => item.Stage == EmbedStage), item => Assert.Equal(WorkItemState.Queued, item.State));
    }

    [Fact]
    public async Task WaitingJob_NewWorkThatNeedsNoModel_RunsAndTheJobWaitsAgain()
    {
        var thumbnails = new TestStageHandler(ThumbnailStage, ResourcePool.Cpu);
        Start(thumbnails, new ModelStage(EmbedStage));
        var jobId = await scheduler!.EnqueueJobAsync(Job([new WorkItemRequest(EmbedStage)]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);

        Assert.True(await scheduler.EnqueueWorkAsync(jobId, [new WorkItemRequest(ThumbnailStage), new WorkItemRequest(ThumbnailStage)], Token));

        await WaitUntilAsync(() => thumbnails.Started.Count == 2);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);
    }

    [Fact]
    public async Task WaitingJob_ModelInstalledWhileTheAppWasClosed_RunsAtTheNextStart()
    {
        Start(new ModelStage(EmbedStage));
        var jobId = await scheduler!.EnqueueJobAsync(Job([new WorkItemRequest(EmbedStage), new WorkItemRequest(EmbedStage)]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);
        await scheduler.DisposeAsync();
        scheduler = null;

        await InstallModelAsync();
        var embed = new ModelStage(EmbedStage);
        Start(embed);

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Equal(2, embed.Started.Count);
    }

    [Fact]
    public async Task WaitingJob_CanBeCancelled()
    {
        Start(new ModelStage(EmbedStage));
        var jobId = await scheduler!.EnqueueJobAsync(Job([new WorkItemRequest(EmbedStage)]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.WaitingForModel);

        Assert.True(await scheduler.CancelJobAsync(jobId, Token));

        await WaitForJobAsync(jobId, ProcessingJobState.Cancelled);
    }

    [Fact]
    public async Task ModelInstalled_StageRunsWithoutWaiting()
    {
        await InstallModelAsync();
        var embed = new ModelStage(EmbedStage);
        Start(embed);

        var jobId = await scheduler!.EnqueueJobAsync(Job([new WorkItemRequest(EmbedStage)]), Token);

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Single(embed.Started);
    }

    private static JobRequest Job(IEnumerable<WorkItemRequest> workItems) => new("test.analysis", "Analyze", JobPriority.Import, [.. workItems]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), "Timed out waiting for the condition.");
            await Task.Delay(10, Token);
        }
    }

    private async Task InstallModelAsync()
    {
        var entry = catalog.Find("embedder")!;
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "vision.onnx");
        await File.WriteAllBytesAsync(source, modelFile, Token);
        await store.ImportAsync(
            new ModelImportRequest(entry.ModelId, entry.Version, entry.DefaultTier, entry.DisplayName, [new ModelFileSource("vision", source, entry.Default.Files[0].Sha256)]),
            null,
            Token);
    }

    private void Start(params IStageHandler[] handlers)
    {
        var limits = new Dictionary<ResourcePool, int> { [ResourcePool.Io] = 2, [ResourcePool.Cpu] = 2, [ResourcePool.Ai] = 1 };
        scheduler = new JobScheduler(bed.Library, handlers, limits, new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance, availability);
        scheduler.Start(startPaused: false);
    }

    private async Task WaitForJobAsync(Id<ProcessingJob> jobId, ProcessingJobState state)
    {
        var stopwatch = Stopwatch.StartNew();
        ProcessingJobState current;
        while ((current = (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State) != state)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Timed out waiting for the job to be {state}; it's {current}.");
            await Task.Delay(20, Token);
        }
    }

    /// <summary>
    /// A stage that needs the embedding model.
    /// </summary>
    private sealed class ModelStage(string stage) : IModelDependentStage
    {
        private readonly TestStageHandler inner = new(stage, ResourcePool.Ai);

        public string Stage => inner.Stage;

        public ResourcePool Pool => inner.Pool;

        public ModelPurpose RequiredModel => ModelPurpose.Embedding;

        public System.Collections.Concurrent.ConcurrentQueue<WorkItem> Started => inner.Started;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken) => inner.HandleAsync(context, cancellationToken);
    }
}
