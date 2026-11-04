using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for creating a library (E02-S01-T01, T04).
/// </summary>
public sealed class CreateLibraryTests
{
    private const string Parent = @"D:\Media";

    private readonly FakeLibraryLocations locations = new();
    private readonly FakeLibraryStore store = new();
    private readonly FakeLibrarySession session = new();
    private readonly FakeSettingsService settings = new();

    [Theory]
    [InlineData("", LibraryLocationError.NameMissing)]
    [InlineData("   ", LibraryLocationError.NameMissing)]
    [InlineData("Family?", LibraryLocationError.NameInvalid)]
    [InlineData("a/b", LibraryLocationError.NameInvalid)]
    [InlineData("Trips.", LibraryLocationError.NameInvalid)]
    [InlineData("..", LibraryLocationError.NameInvalid)]
    public void Rules_RejectBadNames(string name, LibraryLocationError error)
    {
        Assert.Equal(error, LibraryLocationRules.Check(name, Parent, locations));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"relative\folder")]
    public void Rules_RejectAMissingOrRelativeLocation(string? parent)
    {
        Assert.Equal(LibraryLocationError.LocationMissing, LibraryLocationRules.Check("Family", parent, locations));
    }

    [Fact]
    public void Rules_RejectLocationsByWhatTheFileSystemSays()
    {
        LibraryLocationError? Check(LibraryLocationInfo info)
        {
            locations.Info = info;
            return LibraryLocationRules.Check("Family", Parent, locations);
        }

        Assert.Null(Check(new(IsWritable: true, FreeBytes: LibraryLocationRules.MinimumFreeBytes, IsInsideLibrary: false, LibraryExists: false)));
        Assert.Null(Check(new(IsWritable: true, FreeBytes: null, IsInsideLibrary: false, LibraryExists: false)));
        Assert.Equal(LibraryLocationError.NotEnoughSpace, Check(new(true, LibraryLocationRules.MinimumFreeBytes - 1, false, false)));
        Assert.Equal(LibraryLocationError.NotWritable, Check(new(false, 10L << 30, false, false)));
        Assert.Equal(LibraryLocationError.AlreadyExists, Check(new(true, 10L << 30, false, true)));
        Assert.Equal(LibraryLocationError.InsideLibrary, Check(new(true, 10L << 30, true, true)));
    }

    [Fact]
    public async Task Create_CreatesOpensAndRecordsTheLibrary()
    {
        settings.Save(new AppSettings { RecentLibraries = [new RecentLibrary(@"D:\Old.mfc", "Old", DateTimeOffset.UnixEpoch)] });

        var result = await CreateAsync("  Family ");

        Assert.True(result.Succeeded);
        Assert.Equal((Parent, "Family"), Assert.Single(store.Created));
        Assert.Equal(@"D:\Media\Family.mfc", session.Current?.Layout.RootDirectory);
        Assert.Equal(@"D:\Media\Family.mfc", settings.Current.LastLibraryPath);
        Assert.Equal(["Family", "Old"], settings.Current.RecentLibraries!.Select(entry => entry.Name));
    }

    [Fact]
    public async Task Create_InvalidLocation_CreatesNothing()
    {
        locations.Info = locations.Info with { IsWritable = false };

        var result = await CreateAsync("Family");

        Assert.Equal(LibraryLocationError.NotWritable, result.LocationError);
        Assert.Empty(store.Created);
        Assert.Null(session.Current);
        Assert.Null(settings.Current.RecentLibraries);
    }

    [Fact]
    public async Task Create_StoreFailure_IsReportedWithoutOpeningAnything()
    {
        store.Failure = new LibraryOpenException(LibraryOpenError.AlreadyExists, @"D:\Media\Family.mfc");

        var result = await CreateAsync("Family");

        Assert.Equal(LibraryOpenError.AlreadyExists, result.OpenError);
        Assert.Null(session.Current);
    }

    [Fact]
    public async Task Create_DiskFailure_IsReportedAsNotWritable()
    {
        store.Failure = new UnauthorizedAccessException();

        var result = await CreateAsync("Family");

        Assert.Equal(LibraryLocationError.NotWritable, result.LocationError);
    }

    [Fact]
    public async Task Create_RecentListNotSaved_StillSucceeds()
    {
        settings.UpdateFailure = new IOException("The settings file is locked.");

        var result = await CreateAsync("Family");

        Assert.True(result.Succeeded);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public async Task AddFolders_AddsNewFoldersOnce()
    {
        await CreateAsync("Family");
        var handler = AddFoldersHandler();
        FolderHandle photos = new(@"E:\Photos", "Photos");

        var first = await handler.Handle(new AddLibraryFoldersCommand([photos, photos]), CancellationToken.None);
        var second = await handler.Handle(new AddLibraryFoldersCommand([photos, new FolderHandle(@"E:\Videos", "Videos")]), CancellationToken.None);

        Assert.Equal(@"E:\Photos", Assert.Single(first.Added).DisplayPath);
        Assert.Equal(@"E:\Videos", Assert.Single(second.Added).PlatformHandle);
        Assert.Equal(photos, Assert.Single(second.Refused).Folder);
        Assert.Equal(2, ((FakeOpenLibrary)session.Current!).FolderList.Folders.Count);
    }

    [Fact]
    public async Task AddFolders_RefusesAChildAndMergesIntoAParent()
    {
        await CreateAsync("Family");
        var handler = AddFoldersHandler();
        var photos = Assert.Single((await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(@"E:\Photos", "Photos")]), CancellationToken.None)).Added);
        photos.ChangeRules(new FolderRules { ExcludePatterns = [FolderRules.ForSubfolder("raw"), "*.tmp"] });

        var child = await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(@"E:\Photos\2024", "2024")]), CancellationToken.None);
        var parent = await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(@"E:\", "E:")]), CancellationToken.None);

        Assert.Empty(child.Added);
        Assert.Same(photos, Assert.Single(child.Refused).CoveredBy);
        Assert.Same(photos, Assert.Single(parent.Merged));
        var drive = Assert.Single(((FakeOpenLibrary)session.Current!).FolderList.Folders);
        Assert.Same(Assert.Single(parent.Added), drive);
        Assert.Equal(["/Photos/raw", "/Photos/**/*.tmp"], drive.Rules.ExcludePatterns);
    }

    private AddLibraryFoldersCommandHandler AddFoldersHandler()
        => new(session, new FakeFolderSource(), TimeProvider.System, NullLogger<AddLibraryFoldersCommandHandler>.Instance);

    private Task<CreateLibraryResult> CreateAsync(string name)
        => new CreateLibraryCommandHandler(store, locations, session, settings, TimeProvider.System, NullLogger<CreateLibraryCommandHandler>.Instance)
            .Handle(new CreateLibraryCommand(name, Parent), CancellationToken.None);
}
