using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels.Navigation;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Libraries.CreateLibrary;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Navigation;

/// <summary>
/// End-to-end tests of the navigation panel (E13-S02) with real library storage in a temporary folder.
/// </summary>
public sealed class NavigationPanelTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IOpenLibrary Library => app.Services.GetRequiredService<ILibrarySession>().Current!;

    private LibraryItemsSource Source => app.Services.GetRequiredService<LibraryItemsSource>();

    [Fact]
    public async Task Panel_ShowsTheSectionsWithCounts_AndEachEntryScopesTheBrowser()
    {
        var (photos, _) = await CreateLibraryAsync();
        var panel = app.Services.GetRequiredService<NavigationPanelViewModel>();

        await panel.ActivateAsync();

        Assert.Equal(["Library", "Folders"], panel.Sections.Select(section => section.Title));
        var library = panel.Sections[0];
        Assert.Equal(["All media", "Recently imported"], library.Entries.Select(entry => entry.Label));
        Assert.Equal([6, 5], library.Entries.Select(entry => entry.Count));
        Assert.Same(library.Entries[0], panel.SelectedEntry);
        Assert.Equal(6, await Source.CountAsync(Token));

        var folders = panel.Sections[1];
        Assert.Equal(["photos:4", "videos:2"], folders.Entries.Select(entry => $"{entry.Label}:{entry.Count}").Order(StringComparer.Ordinal));

        // Recently imported: the last 7 days (D053).
        panel.Select(library.Entries[1]);
        Assert.Equal(5, await Source.CountAsync(Token));

        // A folder, then one of its subfolders, scope the browser to their items.
        var photosEntry = Entry(panel, "photos");
        panel.Select(photosEntry);
        Assert.Equal(new FolderScope(photos.Id), Source.Scope);
        Assert.Equal(4, await Source.CountAsync(Token));

        photosEntry.IsExpanded = true;
        await photosEntry.LoadChildrenAsync();
        Assert.Equal(["2024:3"], photosEntry.Children.Select(child => $"{child.Label}:{child.Count}"));
        var year = photosEntry.Children[0];
        await year.LoadChildrenAsync();
        Assert.Equal(["trip:2"], year.Children.Select(child => $"{child.Label}:{child.Count}"));
        panel.Select(year.Children[0]);
        Assert.Equal(2, await Source.CountAsync(Token));
        Assert.True(year.Children[0].IsSelected);
        Assert.False(photosEntry.IsSelected);
    }

    [Fact]
    public async Task Refresh_UpdatesCountsAndExpandedSubfolders_AndARemovedFolderFallsBackToAllMedia()
    {
        var (photos, videos) = await CreateLibraryAsync();
        var panel = app.Services.GetRequiredService<NavigationPanelViewModel>();
        await panel.ActivateAsync();
        var photosEntry = Entry(panel, "photos");
        photosEntry.IsExpanded = true;
        await photosEntry.LoadChildrenAsync();
        panel.Select(Entry(panel, "videos"));

        await Library.Items.AddRangeAsync([Item(photos, "2025/new.jpg"), Item(photos, "2024/more.jpg")], Token);
        await app.Services.GetRequiredService<ISender>().Send(new RemoveFolderCommand(videos.Id), Token);
        await panel.RefreshAsync();

        Assert.Equal([6, 5], panel.Sections[0].Entries.Select(entry => entry.Count));
        var entry = Assert.Single(panel.Sections[1].Entries);
        Assert.Same(photosEntry, entry);
        Assert.Equal(6, entry.Count);
        Assert.Equal(["2024:4", "2025:1"], entry.Children.Select(child => $"{child.Label}:{child.Count}"));
        Assert.Same(panel.Sections[0].Entries[0], panel.SelectedEntry);
        Assert.Equal(BrowseScope.All, Source.Scope);
    }

    [Fact]
    public async Task OfflineFolder_IsMarked_AndStillBrowsable()
    {
        var (photos, _) = await CreateLibraryAsync();
        photos.MarkOffline(DateTimeOffset.UtcNow);
        await Library.Folders.UpdateAsync(photos, Token);
        var panel = app.Services.GetRequiredService<NavigationPanelViewModel>();

        await panel.ActivateAsync();

        var entry = Entry(panel, "photos");
        Assert.True(entry.IsOffline);
        Assert.False(Entry(panel, "videos").IsOffline);
        Assert.Contains("Offline", entry.ToolTip, StringComparison.Ordinal);
        panel.Select(entry);
        Assert.Equal(4, await Source.CountAsync(Token));
    }

    [Fact]
    public async Task CollapsingASection_IsRememberedInTheLayout()
    {
        await CreateLibraryAsync();
        var panel = app.Services.GetRequiredService<NavigationPanelViewModel>();
        var layout = app.Services.GetRequiredService<ShellLayout>();

        panel.Sections[1].IsExpanded = false;

        Assert.Equal(["folders"], layout.CollapsedNavigationSections);
        layout.Reset();
        Assert.True(panel.Sections[1].IsExpanded);
    }

    [Fact]
    public async Task AnotherEpic_AddsASectionWithoutChangingThePanel()
    {
        await CreateLibraryAsync();
        var panel = new NavigationPanelViewModel(
            [.. app.Services.GetServices<INavigationContributor>(), new TestContributor()],
            Source,
            app.Services.GetRequiredService<ILibrarySession>(),
            app.Services.GetRequiredService<ShellLayout>(),
            app.Services.GetRequiredService<Ui.Libraries.LibraryDialogs>());

        await panel.RefreshAsync();

        Assert.Equal(["Library", "Saved", "Folders"], panel.Sections.Select(section => section.Title));
        Assert.Equal(1, Assert.Single(panel.Sections[1].Entries).Count);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private static NavigationEntry Entry(NavigationPanelViewModel panel, string label) =>
        panel.Sections.Single(section => section.Key == FolderNavigation.SectionKey).Entries.Single(entry => entry.Label == label);

    private static MediaItem Item(IndexedFolder folder, string path, DateTimeOffset? importedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var item = MediaItem.Discover(folder.Id, RelativePath.From(path), MediaType.Image, 1, now, now, null, importedAt ?? now);
        item.ChangeStatus(MediaItemStatus.Pending, now);
        return item;
    }

    private async Task<(IndexedFolder Photos, IndexedFolder Videos)> CreateLibraryAsync()
    {
        await app.Services.GetRequiredService<ISender>().Send(new CreateLibraryCommand("Family", app.Root), Token);
        var photos = IndexedFolder.Create(Library.Library.Id, Path.Combine(app.Root, "photos"), Path.Combine(app.Root, "photos"), DateTimeOffset.UtcNow);
        var videos = IndexedFolder.Create(Library.Library.Id, Path.Combine(app.Root, "videos"), Path.Combine(app.Root, "videos"), DateTimeOffset.UtcNow);
        await Library.Folders.AddAsync(photos, Token);
        await Library.Folders.AddAsync(videos, Token);
        var excluded = Item(photos, "2024/excluded.jpg");
        excluded.ChangeStatus(MediaItemStatus.Excluded, DateTimeOffset.UtcNow);
        await Library.Items.AddRangeAsync(
            [
                Item(photos, "top.jpg", DateTimeOffset.UtcNow.AddDays(-30)),
                Item(photos, "2024/a.jpg"),
                Item(photos, "2024/trip/b.jpg"),
                Item(photos, "2024/trip/c.jpg"),
                excluded,
                Item(videos, "v1.mp4"),
                Item(videos, "v2.mp4"),
            ],
            Token);
        return (photos, videos);
    }

    private sealed class TestContributor : INavigationContributor
    {
        public NavigationSection Section { get; } = new("saved", "Saved", order: 50);

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            NavigationEntry.Merge(Section.Entries, [new NavigationEntry("one", "Saved search", "Icon.Search", BrowseScope.All) { Count = 1 }]);
            return Task.CompletedTask;
        }
    }
}
