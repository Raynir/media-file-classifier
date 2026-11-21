using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases.Exclusions.GetExcludedItems;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// Tests for the sections of the Settings "Library" page (E17-S04): what each loads, and when.
/// </summary>
public sealed class LibrarySettingsSectionTests
{
    [Fact]
    public async Task ExcludedItems_Attach_LoadsTheList_AndHidesTheDialogChrome()
    {
        var excluded = new ExcludedItemsViewModel(new ExcludedSender(2), Localizer.Current);
        var section = new LibraryExcludedItemsSectionViewModel(new FakeBrowserSession(), excluded, Inline);

        await section.AttachAsync();

        Assert.Equal(2, excluded.Items.Count);
        Assert.True(excluded.IsEmbedded);
        Assert.Same(excluded, section.Content);
    }

    [Fact]
    public async Task ExcludedItems_AttachWithoutALibrary_LoadsNothing_UntilOneOpens()
    {
        var session = new FakeBrowserSession(false);
        var sender = new ExcludedSender(2);
        var section = new LibraryExcludedItemsSectionViewModel(session, new ExcludedItemsViewModel(sender, Localizer.Current), Inline);

        await section.AttachAsync();

        Assert.False(section.HasLibrary);
        Assert.Equal(0, sender.Loads);

        await OpenLibraryAsync(session);

        Assert.True(section.HasLibrary);
        Assert.Equal(1, sender.Loads);
    }

    [Fact]
    public async Task ExcludedItems_Detach_StopsReloadingForANewLibrary()
    {
        var session = new FakeBrowserSession(false);
        var sender = new ExcludedSender(1);
        var section = new LibraryExcludedItemsSectionViewModel(session, new ExcludedItemsViewModel(sender, Localizer.Current), Inline);
        await section.AttachAsync();
        section.Detach();

        await OpenLibraryAsync(session);

        Assert.Equal(0, sender.Loads);
    }

    [Fact]
    public async Task Refresh_WhenHidden_DoesNothing()
    {
        var sender = new ExcludedSender(1);
        var section = new LibraryExcludedItemsSectionViewModel(new FakeBrowserSession(), new ExcludedItemsViewModel(sender, Localizer.Current), Inline);

        await section.RefreshAsync();

        Assert.Equal(0, sender.Loads);
    }

    [Fact]
    public void Folders_AreEmbedded_AndRefreshEveryTwoSeconds()
    {
        var folders = new IndexedFoldersViewModel(new ExcludedSender(0), new RevealPlatform(false), new FileSystemMediaFileSource(), Localizer.Current);

        var section = new LibraryFoldersSectionViewModel(new FakeBrowserSession(), folders, Inline);

        Assert.True(folders.IsEmbedded);
        Assert.Equal(TimeSpan.FromSeconds(2), section.RefreshInterval);
    }

    [Fact]
    public void Information_CopyPath_PutsTheLibraryFolderOnTheClipboard()
    {
        var clipboard = new FakeTextClipboard();
        var section = CreateInformation(new FakeBrowserSession(), new RevealPlatform(true), clipboard);

        section.CopyPathCommand.Execute(null);

        Assert.False(string.IsNullOrEmpty(section.Location));
        Assert.Equal(section.Location, clipboard.Text);
    }

    [Fact]
    public async Task Information_Reveal_ShowsTheFolderInTheFileManager()
    {
        var platform = new RevealPlatform(true);
        var section = CreateInformation(new FakeBrowserSession(), platform, new FakeTextClipboard());

        await section.RevealCommand.ExecuteAsync(null);

        var shown = Assert.Single(platform.Revealed);
        Assert.Equal(section.Location, shown.Folder.Id);
        Assert.Equal(string.Empty, shown.RelativePath);
    }

    [Fact]
    public async Task Information_WithoutALibrary_HasNoLocation_AndNoActions_UntilOneOpens()
    {
        var session = new FakeBrowserSession(false);
        var section = CreateInformation(session, new RevealPlatform(true), new FakeTextClipboard());

        Assert.Equal(string.Empty, section.Location);
        Assert.False(section.RevealCommand.CanExecute(null));
        Assert.False(section.CopyPathCommand.CanExecute(null));

        await OpenLibraryAsync(session);

        Assert.NotEqual(string.Empty, section.Location);
        Assert.True(section.RevealCommand.CanExecute(null));
    }

    [Fact]
    public void Information_WhereThePlatformHasNoFileManager_CannotReveal_ButCanCopy()
    {
        var section = CreateInformation(new FakeBrowserSession(), new RevealPlatform(false), new FakeTextClipboard());

        Assert.False(section.CanReveal);
        Assert.False(section.RevealCommand.CanExecute(null));
        Assert.True(section.CopyPathCommand.CanExecute(null));
    }

    private static void Inline(Action action) => action();

    // The fake session reports the change when it gets a library; it borrows one from a second session.
    private static Task OpenLibraryAsync(FakeBrowserSession session) => session.SetCurrentAsync(new FakeBrowserSession().Current);

    // The statistics view model isn't started in these tests (nothing attaches), so none is needed; the full
    // composition test in LibrarySettingsPageTests covers it.
    private static LibraryInformationSectionViewModel CreateInformation(FakeBrowserSession session, RevealPlatform platform, FakeTextClipboard clipboard)
        => new(session, null!, platform, clipboard, null, Inline);

    private sealed class ExcludedSender(int count) : FakeSender
    {
        public int Loads { get; private set; }

        protected override object? Handle(object request)
        {
            if (request is GetExcludedItemsQuery)
            {
                Loads++;
                return (IReadOnlyList<ExcludedItem>)[.. Enumerable.Range(0, count).Select(index => new ExcludedItem(Id<MediaItem>.New(), $"photo-{index}.jpg", @"C:\Photos", DateTimeOffset.UtcNow))];
            }

            return null;
        }
    }

    private sealed class RevealPlatform(bool canReveal) : IPlatformServices
    {
        public List<FileLocation> Revealed { get; } = [];

        public IAppPaths Paths { get; } = new FakeAppPaths();

        public bool CanRevealInFileExplorer => canReveal;

        public Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult<FolderHandle?>(null);

        public Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileLocation>>([]);

        public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default)
        {
            Revealed.Add(location);
            return Task.CompletedTask;
        }

        public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
