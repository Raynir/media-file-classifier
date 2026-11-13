using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Libraries;

/// <summary>
/// Tests for <see cref="LibraryLocations"/> and <see cref="LibrarySession"/> (E02-S01).
/// </summary>
public sealed class LibraryLocationsTests : IDisposable
{
    private readonly TemporaryFolder temp = new();
    private readonly LibraryLocations locations = new();

    public LibraryLocationsTests()
    {
        Directory.CreateDirectory(temp.Path);
    }

    [Fact]
    public void DefaultParentDirectory_IsInTheUserProfile()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Media File Classifier"),
            locations.DefaultParentDirectory);
    }

    [Fact]
    public void Inspect_MissingFolder_IsJudgedByItsParent()
    {
        var info = locations.Inspect(temp.Combine("new", "deeper"), "Family");

        Assert.True(info.IsWritable);
        Assert.True(info.FreeBytes > 0);
        Assert.False(info.IsInsideLibrary);
        Assert.False(info.LibraryExists);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public void Inspect_FindsAnExistingLibraryAndFoldersInsideLibraries()
    {
        var library = Directory.CreateDirectory(temp.Combine("Family.mfc"));
        File.WriteAllText(Path.Combine(library.FullName, LibraryLayout.DatabaseFileName), string.Empty);

        Assert.True(locations.Inspect(temp.Path, "Family").LibraryExists);
        Assert.False(locations.Inspect(temp.Path, "Trips").LibraryExists);
        Assert.True(locations.Inspect(Path.Combine(library.FullName, "thumbnails"), "Trips").IsInsideLibrary);
    }

    [Fact]
    public void Inspect_EmptyLibraryFolder_DoesNotCountAsExisting()
    {
        Directory.CreateDirectory(temp.Combine("Family.mfc"));

        Assert.False(locations.Inspect(temp.Path, "Family").LibraryExists);
    }

    [Fact]
    public void Inspect_ReadOnlyFolder_IsNotWritable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A read-only attribute on a folder doesn't stop file creation on Windows, so deny it in the ACL instead.
        var folder = Directory.CreateDirectory(temp.Combine("locked"));
        var security = System.IO.FileSystemAclExtensions.GetAccessControl(folder);
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(rule);
        System.IO.FileSystemAclExtensions.SetAccessControl(folder, security);
        try
        {
            Assert.False(locations.Inspect(folder.FullName, "Family").IsWritable);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            System.IO.FileSystemAclExtensions.SetAccessControl(folder, security);
        }
    }

    [Fact]
    public async Task Session_ClosesThePreviousLibraryAndRaisesChanged()
    {
        var session = new LibrarySession();
        var first = new ClosableLibrary();
        var changes = 0;
        session.CurrentChanged += (_, _) => changes++;

        await session.SetCurrentAsync(first);
        await session.SetCurrentAsync(first);
        await session.SetCurrentAsync(null);

        Assert.True(first.IsDisposed);
        Assert.Null(session.Current);
        Assert.Equal(2, changes);
    }

    public void Dispose() => temp.Dispose();

    private sealed class ClosableLibrary : IOpenLibrary
    {
        public bool IsDisposed { get; private set; }

        public LibraryLayout Layout => throw new NotSupportedException();

        public Domain.Libraries.Library Library => throw new NotSupportedException();

        public Abstractions.Repositories.IFolderRepository Folders => throw new NotSupportedException();

        public Abstractions.Repositories.IMediaItemRepository Items => throw new NotSupportedException();

        public Abstractions.Repositories.IItemMetadataRepository Metadata => throw new NotSupportedException();

        public Abstractions.Repositories.IModelRegistrationRepository Models => throw new NotSupportedException();

        public Abstractions.Repositories.IJobRepository Jobs => throw new NotSupportedException();

        public Abstractions.Repositories.IKeyframeRepository Keyframes => throw new NotSupportedException();

        public Abstractions.Repositories.IAnalyzerStateRepository AnalyzerStates => throw new NotSupportedException();

        public Abstractions.Repositories.IEmbeddingRepository Embeddings => throw new NotSupportedException();

        public Abstractions.Search.ISearchStore Search => throw new NotSupportedException();

        public ILibraryMaintenance Maintenance => throw new NotSupportedException();

        public Task UpdateLibraryAsync(Domain.Libraries.Library library, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
