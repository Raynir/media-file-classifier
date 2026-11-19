using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Previews;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;
using SkiaSharp;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the preview and details panel (E14-S05): it follows the focused item, shows the selection count, loads
/// a screen-sized preview then full resolution when zoomed, falls back to the thumbnail (D025), and keeps its
/// layout in the shell layout.
/// </summary>
public sealed class DetailsPanelTests : IDisposable
{
    private readonly TemporaryFolder temp = new();

    public void Dispose()
    {
        temp.Dispose();
    }

    [Fact]
    public Task Panel_FollowsTheFocusedItem_ShowsTheCount_AndLeavesTheRegionWhenNothingIsSelected() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 10);

        bed.Select(2, SelectionGesture.Replace);
        Assert.Equal(bed.Source.Ids[2], bed.Panel.Item?.Id);
        Assert.Same(bed.Panel, bed.Regions.Details);
        Assert.Null(bed.Panel.SelectedCountText);
        Assert.Equal(("IMG_2.png", null), (bed.Panel.Title, bed.Panel.Subtitle));

        bed.Select(4, SelectionGesture.Range);
        Assert.Equal(bed.Source.Ids[4], bed.Panel.Item?.Id);
        Assert.Equal("3 items selected", bed.Panel.SelectedCountText);

        bed.Selection.Clear();
        bed.Settle();
        Assert.Null(bed.Panel.Item);
        Assert.Null(bed.Regions.Details);
    });

    [Fact]
    public Task Preview_DecodesTheOriginalAtScreenSize_ThenAtFullResolutionWhenZoomedIn() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Panel.ScreenPixels = 100;

        bed.Select(1, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.ScreenSized, bed.Panel.PreviewKind);
        Assert.Equal(new PixelSize(100, 75), bed.Panel.PreviewBitmapSize);
        Assert.Equal(ImageOrientation.Rotate90, bed.Panel.PreviewOrientation);
        Assert.Equal(new Size(300, 400), bed.Panel.PreviewImageSize);

        // Fitted in a small preview, the screen-sized bitmap is enough; at 100% it isn't.
        bed.Wait(bed.Panel.OnZoomChangedAsync(0.2));
        Assert.Equal(PreviewKind.ScreenSized, bed.Panel.PreviewKind);
        bed.Wait(bed.Panel.OnZoomChangedAsync(1));
        Assert.Equal(PreviewKind.FullResolution, bed.Panel.PreviewKind);
        Assert.Equal(new PixelSize(400, 300), bed.Panel.PreviewBitmapSize);
    });

    [Fact]
    public Task Preview_WithoutExtractedDimensions_TakesTheOriginalsSize() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3) { WithoutTechnical = true };
        bed.Panel.ScreenPixels = 100;

        bed.Select(1, SelectionGesture.Replace);
        Assert.Equal(new Size(400, 300), bed.Panel.PreviewImageSize);
        bed.Wait(bed.Panel.OnZoomChangedAsync(1));
        Assert.Equal(PreviewKind.FullResolution, bed.Panel.PreviewKind);

        bed.Select(1, SelectionGesture.FocusOnly);
        Assert.Equal(new Size(400, 300), bed.Panel.PreviewImageSize);
    });

    [Theory]
    [InlineData(ItemAvailability.Offline, "Showing the thumbnail: the folder Photos is offline.")]
    [InlineData(ItemAvailability.Missing, "Showing the thumbnail: the file is missing.")]
    public Task Preview_ForAnUnavailableFile_ShowsTheThumbnail(ItemAvailability availability, string note) => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Availability = availability;

        bed.Select(0, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.Thumbnail, bed.Panel.PreviewKind);
        Assert.Equal(ImageOrientation.Normal, bed.Panel.PreviewOrientation);
        Assert.Equal(note, bed.Panel.PreviewNote);
        Assert.Equal(0, bed.Decoder.Decodes);

        // Only the thumbnail shows, so 1:1 is the thumbnail's own pixels, also after a refresh.
        Assert.Equal(default, bed.Panel.PreviewImageSize);
        bed.Select(0, SelectionGesture.FocusOnly);
        Assert.Equal(default, bed.Panel.PreviewImageSize);
    });

    [Fact]
    public Task Preview_OfAnUndecodableFile_KeepsTheThumbnail() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Corrupt = true;

        bed.Select(0, SelectionGesture.Replace);

        Assert.Equal(PreviewKind.Thumbnail, bed.Panel.PreviewKind);
        Assert.Equal("Showing the thumbnail: the file couldn't be read.", bed.Panel.PreviewNote);
    });

    [Fact]
    public Task Reselecting_TheSameItem_KeepsItsPreview() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        var shown = bed.Panel.PreviewBitmap;

        bed.Select(1, SelectionGesture.FocusOnly);

        Assert.Same(shown, bed.Panel.PreviewBitmap);
        Assert.Equal(1, bed.Decoder.Decodes);
    });

    [Fact]
    public Task SelectionRefresh_WhileTheOriginalDecodes_DoesNotStopIt() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);

        // An import refreshes the selection while the item's original is still loading.
        var first = bed.Selection.ApplyAsync(1, SelectionGesture.Replace);
        bed.Wait(first);
        bed.Wait(bed.Selection.ApplyAsync(1, SelectionGesture.FocusOnly));
        Pump.Until(() => bed.Panel.PreviewKind == PreviewKind.FullResolution);

        Assert.Equal(1, bed.Decoder.Decodes);
    });

    [Fact]
    public Task Open_ShowsAHiddenOrCollapsedPanelAndPreview_AndAsksForFocus() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        var focusRequests = 0;
        bed.Panel.FocusRequested += (_, _) => focusRequests++;
        bed.Layout.Details.IsVisible = false;

        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[0]));
        Assert.True(bed.Layout.Details.IsExpanded);

        bed.Layout.Details.Collapse();
        bed.Layout.IsDetailsPreviewCollapsed = true;
        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[0]));
        Assert.True(bed.Layout.Details.IsExpanded);
        Assert.False(bed.Layout.IsDetailsPreviewCollapsed);
        Assert.Equal(2, focusRequests);
    });

    [Fact]
    public Task Reload_AfterAnEditInAnotherView_ShowsTheNewValue_OnlyForTheShownItem() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Select(1, SelectionGesture.Replace);
        bed.ItemTitle = "Rex";

        bed.Panel.Reload([bed.Source.Ids[2]]);
        bed.Settle();
        Assert.Null(bed.Panel.Item?.Title);

        bed.Panel.Reload([bed.Source.Ids[0], bed.Source.Ids[1]]);
        bed.Settle();
        Assert.Equal(("Rex", "IMG_1.png"), (bed.Panel.Title, bed.Panel.Subtitle));
    });

    [Fact]
    public Task Sections_AreKeptInTheShellLayout() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        var changed = new List<string?>();
        bed.Panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        bed.Panel.SelectedTab = 1;
        bed.Panel.TogglePreviewCommand.Execute(null);
        bed.Panel.PreviewHeight = 5_000;

        Assert.Equal((DetailsTab.Technical, true, ShellLayout.MaxPreviewHeight), (bed.Layout.DetailsTab, bed.Layout.IsDetailsPreviewCollapsed, bed.Layout.DetailsPreviewHeight));
        Assert.Equal([nameof(DetailsPanelViewModel.SelectedTab), nameof(DetailsPanelViewModel.IsPreviewExpanded), nameof(DetailsPanelViewModel.PreviewHeight)], changed);
    });

    [Fact]
    public Task View_TakesTheFocus_ShowsTheTabs_AndTheZoomButtonsSwitchBetweenFitAnd100Percent() => TestApp.RunAsync(() =>
    {
        var bed = new Bed(this, 3);
        bed.Panel.ScreenPixels = 100;
        bed.Select(1, SelectionGesture.Replace);

        // Enter in the grid before the view exists: the preview takes the focus once it's shown.
        bed.Wait(bed.Panel.OpenAsync(bed.Source.Ids[1]));
        var view = new DetailsPanelView { DataContext = bed.Panel };
        var window = new Window { Width = 320, Height = 800, Content = view };
        window.Show();
        bed.Settle();
        Dispatcher.UIThread.RunJobs();

        var preview = view.GetVisualDescendants().OfType<ZoomImage>().Single();
        Assert.True(preview.IsFocused);
        Assert.False(bed.Panel.IsFocusPending);
        Assert.True(preview.Zoom < 1);
        view.FindControl<Button>("ActualSizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, preview.Zoom, 6);
        view.FindControl<Button>("FitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(preview.Zoom < 1);

        // The next item, as large as this one, starts fitted.
        view.FindControl<Button>("ActualSizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bed.Select(2, SelectionGesture.Replace);
        Assert.True(preview.Zoom < 1);

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(2, tabs.ItemCount);
        tabs.SelectedIndex = 1;
        Assert.Equal(DetailsTab.Technical, bed.Layout.DetailsTab);
        window.Close();
    });

    private string WriteImage(string name, int width, int height)
    {
        var path = temp.Combine(name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>
    /// A selection over fake results, with the panel and fakes for the details, files and thumbnails.
    /// </summary>
    private sealed class Bed : ISender
    {
        private readonly DetailsPanelTests test;
        private readonly string original;
        private readonly string corrupt;

        public Bed(DetailsPanelTests test, int count)
        {
            this.test = test;
            original = test.WriteImage("original.png", 400, 300);
            corrupt = test.temp.Combine("corrupt.png");
            File.WriteAllBytes(corrupt, [1, 2, 3, 4]);
            Source = new FakeResultSource(count);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(count);
            var thumbnails = new FakeThumbnailCache();
            foreach (var id in Source.Ids)
            {
                thumbnails.Files[(id, ThumbnailSize.Large)] = test.WriteImage($"thumb-{id}.png", 64, 48);
            }

            var loader = new ThumbnailLoader(new FakeBrowserSession(), thumbnails, new ThumbnailBitmapCache());
            var registry = new CommandRegistry([], Localizer.Current, new FakeCapabilityGate());
            Panel = new DetailsPanelViewModel(
                Selection, this, Regions, Layout, new PreviewLoader(loader, new FakeFiles(), Decoder), registry, Localizer.Current, () => throw new NotSupportedException());
        }

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public ShellRegions Regions { get; } = new();

        public ShellLayout Layout { get; } = new();

        public CountingDecoder Decoder { get; } = new();

        public DetailsPanelViewModel Panel { get; }

        public ItemAvailability Availability { get; set; }

        public bool Corrupt { get; set; }

        public bool WithoutTechnical { get; set; }

        public string? ItemTitle { get; set; }

        public void Select(int index, SelectionGesture gesture)
        {
            Wait(Selection.ApplyAsync(index, gesture));
            Settle();
        }

        public void Settle() => Wait(Panel.Loading);

        public void Wait(Task task) => Pump.Until(() => task.IsCompleted);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetItemDetailsQuery>(request);
            var index = Source.Ids.ToList().IndexOf(query.ItemId);
            var path = Corrupt ? corrupt : original;
            var location = new ItemLocation(
                query.ItemId, new FileLocation(new FolderHandle(test.temp.Path, "Photos"), Path.GetFileName(path)), path, "Photos", Availability);
            var technical = new TechnicalMetadata
            {
                ItemId = query.ItemId,
                Width = 400,
                Height = 300,
                Orientation = ImageOrientation.Rotate90,
                ExtractedAt = DateTimeOffset.UnixEpoch,
            };
            var details = new ItemDetails(
                query.ItemId, $"IMG_{index}.png", "png", MediaType.Image, 1000, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MediaItemStatus.Indexed, null, location, WithoutTechnical ? null : technical, ItemTitle, null, []);
            return Task.FromResult((TResponse)(object)details);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Decodes with Skia, counting the decodes of screen-sized previews.
    /// </summary>
    private sealed class CountingDecoder : IPreviewDecoder
    {
        private readonly SkiaPreviewDecoder inner = new();

        public int Decodes { get; private set; }

        public Task<DecodedImage> DecodeAsync(MediaContent content, int? maxLongestEdge, CancellationToken cancellationToken)
        {
            if (maxLongestEdge is not null)
            {
                Decodes++;
            }

            return inner.DecodeAsync(content, maxLongestEdge, cancellationToken);
        }
    }

    /// <summary>
    /// Files are read from their full paths, so the file source isn't used.
    /// </summary>
    private sealed class FakeFiles : IMediaFileSource
    {
        public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string? GetPathInside(FolderHandle folder, FolderHandle parent) => throw new NotSupportedException();
    }
}
