using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Controls;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views.Browser;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for how Offline and Missing items show in the grid and the list (E14-S07-T01, D025): their badges, the
/// dimmed thumbnail and text, and that the two states look different in both themes.
/// </summary>
public sealed class OfflineItemDisplayTests
{
    [Fact]
    public void Badges_OfAnAvailableItem_AreOnlyItsStatus()
    {
        var item = Summary(MediaItemStatus.Pending);

        Assert.Equal(["Pending"], TileBadge.For(item).Select(badge => badge.Kind));
        Assert.Empty(TileBadge.For(Summary(MediaItemStatus.Indexed)));
    }

    [Fact]
    public void Badges_OfAnOfflineItem_StartWithOffline_NamingTheFolder()
    {
        var item = Summary(MediaItemStatus.Pending, offline: true);

        var badges = TileBadge.For(item);

        Assert.Equal(["Offline", "Pending"], badges.Select(badge => badge.Kind));
        Assert.Equal(("Offline", @"Offline: folder E:\Photos"), (badges[0].Label, badges[0].ToolTip));
    }

    [Fact]
    public void Badges_OfAMissingItem_AreMissing_WithTheCleanupHint()
    {
        var badges = TileBadge.For(Summary(MediaItemStatus.Missing));

        var badge = Assert.Single(badges);
        Assert.Equal(("Missing", "Missing"), (badge.Kind, badge.Label));
        Assert.Contains("Remove missing items", badge.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Badges_OfAMissingItemInAnOfflineFolder_AreOffline()
    {
        // While the folder is Offline nobody knows whether the file is there, as in the details panel.
        Assert.Equal(["Offline"], TileBadge.For(Summary(MediaItemStatus.Missing, offline: true)).Select(badge => badge.Kind));
    }

    [Theory]
    [InlineData(MediaItemStatus.Indexed, false, false, "Indexed")]
    [InlineData(MediaItemStatus.Indexed, true, true, "Offline")]
    [InlineData(MediaItemStatus.Missing, false, true, "Missing")]
    [InlineData(MediaItemStatus.Error, true, true, "Offline")]
    public void Tile_IsUnavailable_ForOfflineAndMissingItems(MediaItemStatus status, bool offline, bool unavailable, string statusText)
    {
        var tile = new ThumbnailTileViewModel(new ThumbnailLoader(new FakeBrowserSession(), new FakeThumbnailCache(), new ThumbnailBitmapCache()), 100, 75);

        tile.Show(0, Summary(status, offline), ThumbnailSize.Small);

        Assert.Equal((unavailable, statusText), (tile.IsUnavailable, tile.StatusText));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GridAndList_DimTheThumbnailAndText_ButNotTheBadge(bool list) => TestApp.RunAsync(() =>
    {
        var (grid, source) = ListViewTests.Create(3);
        grid.IsListView = list;
        source.Override = index => Summary(index == 0 ? MediaItemStatus.Indexed : MediaItemStatus.Missing, offline: index == 1) with { Id = source.Ids[index] };
        var (window, view) = ListViewTests.Show(grid);

        var shells = Shells(view, list);
        var available = shells.Single(shell => ((ThumbnailTileViewModel)shell.DataContext!).Index == 0);
        var offline = shells.Single(shell => ((ThumbnailTileViewModel)shell.DataContext!).Index == 1);
        Assert.Equal(1, Thumbnail(available).Opacity);
        Assert.Equal(0.5, Thumbnail(offline).Opacity);
        Assert.Equal(1, Badge(offline, "Offline").Opacity);
        window.Close();
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task OfflineAndMissingBadges_LookDifferent_InBothThemes(string theme) => TestApp.RunAsync(() =>
    {
        var application = Application.Current!;
        var previous = application.RequestedThemeVariant;
        application.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            var (grid, source) = ListViewTests.Create(2);
            grid.IsListView = false;
            source.Override = index => Summary(MediaItemStatus.Missing, offline: index == 0) with { Id = source.Ids[index] };
            var (window, view) = ListViewTests.Show(grid);

            var shells = Shells(view, list: false);
            var offline = Badge(shells.Single(shell => ((ThumbnailTileViewModel)shell.DataContext!).Index == 0), "Offline");
            var missing = Badge(shells.Single(shell => ((ThumbnailTileViewModel)shell.DataContext!).Index == 1), "Missing");
            var offlineColor = Assert.IsAssignableFrom<ISolidColorBrush>(offline.Background).Color;
            var missingColor = Assert.IsAssignableFrom<ISolidColorBrush>(missing.Background).Color;
            Assert.NotEqual(offlineColor, missingColor);
            Assert.NotEqual(
                Assert.IsAssignableFrom<ISolidColorBrush>(((Avalonia.Controls.Shapes.Path)offline.Child!).Fill).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(((Avalonia.Controls.Shapes.Path)missing.Child!).Fill).Color);
            window.Close();
        }
        finally
        {
            application.RequestedThemeVariant = previous;
        }
    });

    private static MediaItemSummary Summary(MediaItemStatus status, bool offline = false) =>
        FakeResultSource.Summary(default, 1, status) with { Folder = @"E:\Photos", IsFolderOffline = offline };

    private static List<Border> Shells(ThumbnailGridView view, bool list) =>
        [.. view.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains(list ? "ListRow" : "ThumbnailTile") && border.IsEffectivelyVisible && border.DataContext is ThumbnailTileViewModel { Item: not null })];

    private static MediaThumbnail Thumbnail(Border shell) => shell.GetVisualDescendants().OfType<MediaThumbnail>().First();

    private static Border Badge(Border shell, string kind) =>
        shell.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("StatusBadge") && border.Classes.Contains(kind));
}
