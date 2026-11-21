using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries.DeleteLibrary;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for <see cref="DeleteLibraryCommand"/> and <see cref="GetLibraryDeletionInfoQuery"/> (E02-S07-T01): the open
/// library is closed first, the recent list forgets the library, and a library in use is refused.
/// </summary>
public sealed class DeleteLibraryTests : IDisposable
{
    private const string Family = @"D:\Family.mfc";

    private readonly FakeLibrarySession session = new();
    private readonly FakeSettingsService settings = new();
    private readonly FakeLibraryDeleter deleter = new();
    private readonly ServiceProvider services;

    public DeleteLibraryTests()
    {
        services = new ServiceCollection()
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>))
            .AddUseCases()
            .AddSingleton<ILibrarySession>(session)
            .AddSingleton<ISettingsService>(settings)
            .AddSingleton<ILibraryDeleter>(deleter)
            .BuildServiceProvider();
        settings.Save(new AppSettings
        {
            LastLibraryPath = Family,
            RecentLibraries = [new RecentLibrary(Family, "Family", DateTimeOffset.UnixEpoch), new RecentLibrary(@"D:\Work.mfc", "Work", DateTimeOffset.UnixEpoch)],
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    [Fact]
    public async Task Delete_TheOpenLibrary_ClosesItBeforeDeleting()
    {
        var open = new FakeOpenLibrary(LibraryLayout.FromDirectory(Family), "Family");
        await session.SetCurrentAsync(open);
        deleter.OnDelete = () => Assert.Null(session.Current);

        var result = await Sender.Send(new DeleteLibraryCommand(@"d:\family.mfc\"), Token);

        Assert.True(result.Deleted);
        Assert.Null(session.Current);
        Assert.Equal([@"d:\family.mfc\"], deleter.Deleted);
    }

    [Fact]
    public async Task Delete_AnotherLibrary_LeavesTheOpenOneOpen()
    {
        var open = new FakeOpenLibrary(LibraryLayout.FromDirectory(@"D:\Work.mfc"), "Work");
        await session.SetCurrentAsync(open);

        await Sender.Send(new DeleteLibraryCommand(Family), Token);

        Assert.Same(open, session.Current);
        Assert.False(open.IsDisposed);
    }

    [Fact]
    public async Task Delete_RemovesTheLibraryFromTheRecentList_AndForgetsItForStartup()
    {
        deleter.Result = new LibraryDeletionResult(FolderRemoved: false, KeptEntries: ["notes.txt"]);

        var result = await Sender.Send(new DeleteLibraryCommand(Family), Token);

        Assert.Equal(new DeleteLibraryResult(null, false, ["notes.txt"]).KeptEntries, result.KeptEntries);
        Assert.Equal("Work", Assert.Single(settings.Current.RecentLibraries!).Name);
        Assert.Null(settings.Current.LastLibraryPath);
    }

    [Fact]
    public async Task Delete_LibraryInUse_ReportsIt_AndKeepsItInTheRecentList()
    {
        deleter.Failure = LibraryOpenError.InUse;

        var result = await Sender.Send(new DeleteLibraryCommand(Family), Token);

        Assert.False(result.Deleted);
        Assert.Equal(LibraryOpenError.InUse, result.Error);
        Assert.Equal(2, settings.Current.RecentLibraries!.Count);
        Assert.Equal(Family, settings.Current.LastLibraryPath);
    }

    [Fact]
    public async Task Info_TheOpenLibrary_CountsItsItems_WithoutTheDeleter()
    {
        var open = new FakeOpenLibrary(LibraryLayout.FromDirectory(Family), "Family");
        open.ItemList.Items.Add(Domain.Media.MediaItem.Discover(
            Domain.Identity.Id<Domain.Libraries.IndexedFolder>.New(),
            Domain.Media.RelativePath.From("a.jpg"),
            Domain.Media.MediaType.Image,
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            1,
            DateTimeOffset.UnixEpoch));
        await session.SetCurrentAsync(open);

        var info = await Sender.Send(new GetLibraryDeletionInfoQuery(Family), Token);

        Assert.Equal(new LibraryDeletionInfo(Family, "Family", 1, IsOpen: true, Error: null), info);
        Assert.Empty(deleter.Inspected);
    }

    [Fact]
    public async Task Info_AClosedLibrary_ComesFromTheDeleter()
    {
        var info = await Sender.Send(new GetLibraryDeletionInfoQuery(Family), Token);

        Assert.Equal(new LibraryDeletionInfo(Family, "Family", 42, IsOpen: false, Error: null), info);
    }

    [Fact]
    public async Task Info_LibraryOpenElsewhere_ReportsInUse()
    {
        deleter.Failure = LibraryOpenError.InUse;

        var info = await Sender.Send(new GetLibraryDeletionInfoQuery(Family), Token);

        Assert.Equal(LibraryOpenError.InUse, info.Error);
        Assert.Null(info.Name);
    }

    public void Dispose() => services.Dispose();

    private sealed class FakeLibraryDeleter : ILibraryDeleter
    {
        public LibraryOpenError? Failure { get; set; }

        public LibraryDeletionResult Result { get; set; } = new(FolderRemoved: true, KeptEntries: []);

        public Action? OnDelete { get; set; }

        public List<string> Deleted { get; } = [];

        public List<string> Inspected { get; } = [];

        public Task<LibraryDeletionPreview> InspectAsync(string libraryDirectory, CancellationToken cancellationToken = default)
        {
            Inspected.Add(libraryDirectory);
            return Failure is { } error
                ? Task.FromException<LibraryDeletionPreview>(new LibraryOpenException(error, libraryDirectory))
                : Task.FromResult(new LibraryDeletionPreview("Family", 42));
        }

        public Task<LibraryDeletionResult> DeleteAsync(string libraryDirectory, CancellationToken cancellationToken = default)
        {
            OnDelete?.Invoke();
            if (Failure is { } error)
            {
                return Task.FromException<LibraryDeletionResult>(new LibraryOpenException(error, libraryDirectory));
            }

            Deleted.Add(libraryDirectory);
            return Task.FromResult(Result);
        }
    }
}
