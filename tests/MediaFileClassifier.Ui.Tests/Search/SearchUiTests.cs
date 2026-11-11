using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Views.Search;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Search;

/// <summary>
/// Tests for the search box, the notice above the results and the match reasons (E09-S01-T04, T05), and the
/// reference image input (E09-S04-T01).
/// </summary>
public sealed class SearchUiTests
{
    private readonly Localizer localizer = new(new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
    };

    [Theory]
    [InlineData(SearchFields.Tags, false, false, "Matched in: Tags")]
    [InlineData(SearchFields.OcrText | SearchFields.Title, false, false, "Matched in: Title, Text in the image")]
    [InlineData(SearchFields.FileName, true, false, "Matched in: File name · Visual match")]
    [InlineData(SearchFields.None, true, false, "Visual match")]
    [InlineData(SearchFields.None, true, true, "Visual match · Less relevant")]
    [InlineData(SearchFields.None, false, false, null)]
    public void MatchReason_NamesTheFieldsAndTheVisualMatch(SearchFields fields, bool semantic, bool lessRelevant, string? expected)
    {
        Assert.Equal(expected, MatchReasonText.For(new ResultMatch(fields, semantic, lessRelevant), localizer));
    }

    [Fact]
    public void MatchReason_WhileBrowsing_IsNothing() => Assert.Null(MatchReasonText.For(null, localizer));

    [Fact]
    public Task Notice_FollowsTheSearch_AndTogglesTheLessRelevantResults() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var notice = new SearchNoticeViewModel(source, localizer);
        Assert.False(notice.IsVisible);

        sender.Total = 3;
        sender.LessRelevant = 12;
        sender.Semantic = SemanticSearchStatus.ModelMissing;
        source.SetText("sign");
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.IsVisible);

        Assert.Equal("3 results", notice.Message);
        Assert.Equal("Show 12 less relevant results", notice.LessRelevantText);
        Assert.Contains("only text matches", notice.Hint, StringComparison.Ordinal);

        notice.ToggleLessRelevantCommand.Execute(null);
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.LessRelevantText == "Hide less relevant results");
        Assert.True(sender.LastQuery!.ShowLessRelevant);

        source.SetText(string.Empty);
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => !notice.IsVisible);
    });

    [Fact]
    public Task Notice_NoResultsAndNothingLessRelevant_HidesTheOption() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var notice = new SearchNoticeViewModel(source, localizer);

        source.SetText("zzzz");
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.IsVisible);

        Assert.Equal("No results", notice.Message);
        Assert.Null(notice.LessRelevantText);
        Assert.Null(notice.Hint);
    });

    [Fact]
    public Task SearchBar_DoesNotSearchWhileTyping_SearchesOnEnter_AndClearGoesBackToBrowsing() => TestApp.RunAsync(() =>
    {
        var source = CreateSource(new StubSender());
        var bar = CreateBar(source, new StubSender());

        bar.Text = "bea";
        bar.Text = "beach";

        // Typing alone does not search: the pause timer does it, and Enter does it at once.
        Assert.Equal(string.Empty, source.Text);

        bar.Text = "lake";
        bar.SubmitCommand.Execute(null);
        Assert.Equal("lake", source.Text);

        Assert.True(bar.ClearCommand.CanExecute(null));
        bar.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal(string.Empty, source.Text);
        Assert.False(bar.ClearCommand.CanExecute(null));
    });

    [Fact]
    public Task SearchBar_PickedImage_ReplacesTheText_ShowsTheChip_AndSearchesWithIt() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var picker = new StubPicker { Picture = new PickedImage(new byte[] { 1, 2, 3 }, "beach.jpg") };
        var bar = CreateBar(source, sender, picker);
        bar.Text = "lake";
        bar.SubmitCommand.Execute(null);

        bar.PickImageCommand.Execute(null);
        Pump.Until(() => bar.HasReferenceImage);

        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal("beach.jpg", bar.ReferenceImage!.Name);
        Assert.Same(bar.ReferenceImage, source.ReferenceImage);
        Assert.Equal(string.Empty, source.Text);
        Assert.Equal("beach.jpg", sender.LastLoad!.Name);
        Assert.True(bar.ClearCommand.CanExecute(null));

        // The empty box must not start a text search that drops the picture.
        Pump.For(TimeSpan.FromMilliseconds(100));
        Assert.Same(bar.ReferenceImage, source.ReferenceImage);

        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => sender.LastQuery is { ReferenceImage: not null });
        Assert.Same(bar.ReferenceImage, sender.LastQuery!.ReferenceImage);
    });

    [Fact]
    public Task SearchBar_TypingAfterAnImage_RemovesIt() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var bar = CreateBar(source, sender);
        _ = bar.UseImageAsync(new PickedImage(new byte[] { 1 }, "cat.png"));
        Pump.Until(() => bar.HasReferenceImage);

        bar.Text = "dog";

        Assert.False(bar.HasReferenceImage);
        Assert.Null(source.ReferenceImage);
        bar.SubmitCommand.Execute(null);
        Assert.Equal("dog", source.Text);
    });

    [Theory]
    [InlineData(ReferenceImageProblem.UnsupportedFormat, "isn't a supported image")]
    [InlineData(ReferenceImageProblem.TooLarge, "too large")]
    [InlineData(ReferenceImageProblem.Unreadable, "couldn't be read")]
    public Task SearchBar_RefusedImage_ShowsWhy_AndKeepsTheSearch(ReferenceImageProblem problem, string expected) => TestApp.RunAsync(() =>
    {
        var sender = new StubSender { LoadProblem = problem };
        var source = CreateSource(sender);
        var bar = CreateBar(source, sender);
        bar.Text = "lake";
        bar.SubmitCommand.Execute(null);

        _ = bar.UseImageAsync(new PickedImage(new byte[] { 1 }, "clip.mp4"));
        Pump.Until(() => bar.HasImageError);

        Assert.Contains(expected, bar.ImageError, StringComparison.Ordinal);
        Assert.False(bar.HasReferenceImage);
        Assert.Equal("lake", bar.Text);
        Assert.Equal("lake", source.Text);

        bar.Text = "lakes";
        Assert.False(bar.HasImageError);
    });

    [Fact]
    public Task SearchBar_PickerCancelledOrFailing_ChangesNothingOrShowsTheError() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var picker = new StubPicker();
        var bar = CreateBar(CreateSource(sender), sender, picker);

        bar.PickImageCommand.Execute(null);
        Pump.For(TimeSpan.FromMilliseconds(50));
        Assert.False(bar.HasReferenceImage);
        Assert.False(bar.HasImageError);
        Assert.Null(sender.LastLoad);

        picker.Failure = new IOException("locked");
        bar.PickImageCommand.Execute(null);
        Pump.Until(() => bar.HasImageError);
        Assert.Contains("couldn't be read", bar.ImageError, StringComparison.Ordinal);
    });

    [Fact]
    public Task SearchBar_ClearAndRemove_GoBackToBrowsing() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var bar = CreateBar(source, sender);

        _ = bar.UseImageAsync(new PickedImage(new byte[] { 1 }, "a.png"));
        Pump.Until(() => bar.HasReferenceImage);
        bar.ClearCommand.Execute(null);
        Assert.False(bar.HasReferenceImage);
        Assert.Null(source.ReferenceImage);
        Assert.False(bar.ClearCommand.CanExecute(null));

        _ = bar.UseImageAsync(new PickedImage(new byte[] { 1 }, "b.png"));
        Pump.Until(() => bar.HasReferenceImage);
        bar.RemoveImageCommand.Execute(null);
        Assert.False(bar.HasReferenceImage);
        Assert.Null(source.ReferenceImage);
    });

    [Fact]
    public void Source_ImageAndText_ReplaceEachOther()
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var image = Image("a.png");
        var changes = 0;
        source.Changed += (_, _) => changes++;

        source.SetReferenceImage(image);
        Assert.Same(image, source.ReferenceImage);
        Assert.Equal(1, changes);

        // The same picture again and a blank text don't restart the search.
        source.SetReferenceImage(image);
        source.SetText("  ");
        Assert.Equal(1, changes);
        Assert.Same(image, source.ReferenceImage);

        source.SetShowLessRelevant(true);
        Assert.Same(image, source.ReferenceImage);

        source.SetText("sea");
        Assert.Null(source.ReferenceImage);
        Assert.Equal("sea", source.Text);

        // Removing a picture that isn't there leaves the text search alone.
        source.SetReferenceImage(null);
        Assert.Equal("sea", source.Text);

        source.SetReferenceImage(image);
        source.SetReferenceImage(null);
        Assert.Null(source.ReferenceImage);
        Assert.Equal(string.Empty, source.Text);
        Assert.Null(source.Summary);
    }

    [Fact]
    public Task SearchBarView_DroppedImage_SearchesWithIt_AndNeverReachesTheDropTargetBehind() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var bar = CreateBar(source, sender);
        var view = new SearchBarView { DataContext = bar, Width = 400, Height = 32, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };

        // The import drop (E03-S06) would sit behind the toolbar; it must never see a drop on the search bar.
        var behind = new Grid { Background = Avalonia.Media.Brushes.Transparent, Children = { view } };
        DragDrop.SetAllowDrop(behind, true);
        var importedDrops = 0;
        behind.AddHandler(DragDrop.DropEvent, (_, _) => importedDrops++);
        var window = new Window { Content = behind, Width = 400, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DataFormat.Bitmap, (Avalonia.Media.Imaging.Bitmap)new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul)));
            var point = new Point(200, 16);
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
            Pump.Until(() => bar.HasReferenceImage);

            Assert.Equal(SearchBarView.PastedImageName, bar.ReferenceImage!.Name);
            Assert.NotNull(sender.LastLoad);
            Assert.Same(bar.ReferenceImage, source.ReferenceImage);
            Assert.Equal(0, importedDrops);
        }
        finally
        {
            window.Close();
        }
    });

    private static ReferenceImage Image(string name) => new(new AiImage(new byte[] { 1 }, "image/png", name), name);

    private SearchBarViewModel CreateBar(SearchResultSource source, StubSender sender, StubPicker? picker = null) =>
        new(source, sender, picker ?? new StubPicker(), localizer, TimeSpan.FromMilliseconds(30));

    private static SearchResultSource CreateSource(StubSender sender) =>
        new(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender);

    private sealed class StubSender : ISender
    {
        public int Total { get; set; }

        public int LessRelevant { get; set; }

        public SemanticSearchStatus Semantic { get; set; } = SemanticSearchStatus.Used;

        public SearchMediaQuery? LastQuery { get; private set; }

        public ReferenceImageProblem LoadProblem { get; set; }

        public LoadReferenceImageQuery? LastLoad { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is LoadReferenceImageQuery load)
            {
                LastLoad = load;
                var loaded = LoadProblem == ReferenceImageProblem.None
                    ? new ReferenceImageLoad(new ReferenceImage(new AiImage(load.Content, "image/png", load.Name), load.Name), ReferenceImageProblem.None)
                    : new ReferenceImageLoad(null, LoadProblem);
                return Task.FromResult((TResponse)(object)loaded);
            }

            if (request is SearchMediaQuery query)
            {
                LastQuery = query;
                return Task.FromResult((TResponse)(object)new MediaSearchResults([], Total, [], SearchMode.Hybrid, Semantic, LessRelevant));
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPicker : IReferenceImagePicker
    {
        public PickedImage? Picture { get; set; }

        public Exception? Failure { get; set; }

        public Task<PickedImage?> PickAsync() => Failure is { } failure ? Task.FromException<PickedImage?>(failure) : Task.FromResult(Picture);
    }
}
