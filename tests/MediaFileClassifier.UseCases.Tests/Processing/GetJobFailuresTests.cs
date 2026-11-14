using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Processing.GetJobFailures;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Processing;

/// <summary>
/// Tests for the failures of a job in the processing panel (E05-S03-T03).
/// </summary>
public sealed class GetJobFailuresTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public GetJobFailuresTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Query_WithoutLibrary_IsEmpty() =>
        Assert.Empty(await Sender.Send(new GetJobFailuresQuery(Id<ProcessingJob>.New()), Token));

    [Fact]
    public async Task Query_NamesTheFileAndTheError_AndKeepsWholeTargetFailuresWithoutAnItem()
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "failures"), "failures");
        var item = MediaItem.Discover(Id<MediaFileClassifier.Domain.Libraries.IndexedFolder>.New(), RelativePath.From("album/broken.jpg"), MediaType.Image, 100, Now, Now, null, Now);
        library.ItemList.Items.Add(item);
        var job = Id<ProcessingJob>.New();
        var damaged = WorkItem.Create(job, item.Id, "import.media", ResourcePool.Cpu, 0, null, Now) with { LastError = "Unsupported JPEG", ErrorCategory = ErrorCategory.Permanent };
        var folder = WorkItem.Create(job, null, "import.discover", ResourcePool.Io, 0, null, Now) with { LastError = null };
        library.JobList.FailedWork.AddRange([damaged, folder]);
        await session.SetCurrentAsync(library);

        var failures = await Sender.Send(new GetJobFailuresQuery(job), Token);

        Assert.Equal(2, failures.Count);
        Assert.Equal(("broken.jpg", "album/broken.jpg", "import.media", "Unsupported JPEG", ErrorCategory.Permanent), (failures[0].FileName, failures[0].RelativePath, failures[0].Stage, failures[0].Error, failures[0].Category));
        Assert.Equal(item.Id, failures[0].ItemId);
        Assert.Null(failures[1].ItemId);
        Assert.Null(failures[1].FileName);
        Assert.Equal(string.Empty, failures[1].Error);
    }
}
