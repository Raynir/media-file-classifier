using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the keyframe strip and its moment (E14-S13): the keyframes load for videos and animated images, choosing
/// one raises the seek, and without in-app playback it selects the moment and "Open in player" opens the file and
/// copies the time (D026).
/// </summary>
public sealed class KeyframeStripViewModelTests
{
    private readonly FakeKeyframes keyframes = new();
    private readonly RecordingClipboard clipboard = new();
    private int opened;

    [Theory]
    [InlineData(MediaType.Video)]
    [InlineData(MediaType.AnimatedImage)]
    public Task Show_VideoOrAnimation_LoadsTheKeyframesInOrder(MediaType type) => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 5), Marker(1, 157), Marker(2, 3725)];
        using var strip = Create();

        strip.Show(Item(type, "a.mp4"));
        Pump.Until(() => strip.HasKeyframes);

        Assert.True(strip.IsVisible);
        Assert.False(strip.IsEmpty);
        Assert.Equal(["00:05", "02:37", "1:02:05"], strip.Keyframes.Select(keyframe => keyframe.TimeText));
        Assert.Equal("Keyframe at 02:37", strip.Keyframes[1].Name);
    });

    [Fact]
    public Task Show_AnImage_HidesTheStripAndAsksNothing() => TestApp.RunAsync(() =>
    {
        using var strip = Create();

        strip.Show(Item(MediaType.Image, "a.jpg"));
        Pump.For(TimeSpan.FromMilliseconds(30));

        Assert.False(strip.IsVisible);
        Assert.False(strip.IsEmpty);
        Assert.Empty(keyframes.Asked);
    });

    [Fact]
    public Task Show_VideoWithoutKeyframes_IsVisibleButEmpty() => TestApp.RunAsync(() =>
    {
        using var strip = Create();

        strip.Show(Item(MediaType.Video, "a.mp4"));
        Pump.For(TimeSpan.FromMilliseconds(30));

        Assert.True(strip.IsVisible);
        Assert.True(strip.IsEmpty);
    });

    [Fact]
    public Task Show_AnotherItem_DropsTheKeyframesAndTheMoment_AndIgnoresALateAnswer() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 10)];
        keyframes.Gate = new TaskCompletionSource();
        using var strip = Create();
        strip.Show(Item(MediaType.Video, "a.mp4"));
        strip.Show(Item(MediaType.Image, "b.jpg"));

        keyframes.Gate.SetResult();
        Pump.For(TimeSpan.FromMilliseconds(50));

        Assert.Empty(strip.Keyframes);
        Assert.Null(strip.Moment);
    });

    [Fact]
    public Task Show_WhenTheQueryFails_LeavesTheStripEmpty() => TestApp.RunAsync(() =>
    {
        keyframes.Failure = new InvalidOperationException("The library closed.");
        using var strip = Create();

        strip.Show(Item(MediaType.Video, "a.mp4"));
        Pump.For(TimeSpan.FromMilliseconds(50));

        Assert.True(strip.IsEmpty);
    });

    [Fact]
    public Task Choose_RaisesTheTime_SelectsTheFrame_AndWithPlaybackShowsNoMomentText() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 10), Marker(1, 157)];
        using var strip = Loaded();
        var chosen = new List<TimeSpan>();
        strip.Chosen += (_, time) => chosen.Add(time);

        strip.ChooseCommand.Execute(strip.Keyframes[1]);

        Assert.Equal([TimeSpan.FromSeconds(157)], chosen);
        Assert.False(strip.Keyframes[0].IsSelected);
        Assert.True(strip.Keyframes[1].IsSelected);
        Assert.Null(strip.MomentText);
    });

    [Fact]
    public Task Choose_Fallback_ShowsTheSelectedMoment_AndOffersOpenInPlayer() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 157)];
        using var strip = Loaded();
        strip.IsFallback = true;
        strip.IsFileAvailable = true;

        strip.ChooseCommand.Execute(strip.Keyframes[0]);

        Assert.True(strip.CanOpenPlayer);
        Assert.Equal("Selected moment: 02:37", strip.MomentText);
        Assert.Null(clipboard.Text);
        Assert.Equal(0, opened);
    });

    [Fact]
    public Task OpenInPlayer_WithAMoment_OpensTheFileAndCopiesTheTime() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 157)];
        using var strip = Loaded();
        strip.IsFallback = true;
        strip.IsFileAvailable = true;
        strip.ChooseCommand.Execute(strip.Keyframes[0]);

        var task = strip.OpenInPlayerAsync();
        Pump.Until(() => task.IsCompleted);

        Assert.Equal(1, opened);
        Assert.Equal("02:37", clipboard.Text);
        Assert.Equal("Copied 02:37. Seek to it in your player.", strip.MomentText);
    });

    [Fact]
    public Task OpenInPlayer_WithoutAMoment_OnlyOpensTheFile() => TestApp.RunAsync(() =>
    {
        using var strip = Loaded();
        strip.IsFallback = true;
        strip.IsFileAvailable = true;

        var task = strip.OpenInPlayerAsync();
        Pump.Until(() => task.IsCompleted);

        Assert.Equal(1, opened);
        Assert.Null(clipboard.Text);
        Assert.Null(strip.MomentText);
    });

    [Fact]
    public Task OpenInPlayer_ClipboardFails_StillOpensAndKeepsTheSelectedText() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [Marker(0, 157)];
        clipboard.Failure = new InvalidOperationException("No window.");
        using var strip = Loaded();
        strip.IsFallback = true;
        strip.IsFileAvailable = true;
        strip.ChooseCommand.Execute(strip.Keyframes[0]);

        var task = strip.OpenInPlayerAsync();
        Pump.Until(() => task.IsCompleted);

        Assert.Equal(1, opened);
        Assert.Equal("Selected moment: 02:37", strip.MomentText);
    });

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task CanOpenPlayer_NeedsTheFallbackAndAFile(bool fallback, bool available) => TestApp.RunAsync(() =>
    {
        using var strip = Loaded();

        strip.IsFallback = fallback;
        strip.IsFileAvailable = available;

        Assert.False(strip.CanOpenPlayer);
        Assert.False(strip.OpenInPlayerCommand.CanExecute(null));
    });

    [Fact]
    public Task CanOpenPlayer_FallbackKnownBeforeTheStripShows_TurnsOnWhenItShows() => TestApp.RunAsync(() =>
    {
        using var strip = Create();
        strip.IsFallback = true;
        strip.IsFileAvailable = true;
        var changed = new List<string?>();
        strip.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(strip.CanOpenPlayer);

        strip.Show(Item(MediaType.Video, "a.mp4"));

        Assert.True(strip.CanOpenPlayer);
        Assert.Contains(nameof(KeyframeStripViewModel.CanOpenPlayer), changed);
        Assert.True(strip.OpenInPlayerCommand.CanExecute(null));
    });

    private static KeyframeMarker Marker(int index, int seconds) => new(Id<Keyframe>.New(), index, TimeSpan.FromSeconds(seconds));

    private static ItemDetails Item(MediaType type, string fileName)
    {
        var id = Id<MediaItem>.New();
        var location = new ItemLocation(id, new FileLocation(new FolderHandle(@"C:\media", "media"), fileName), @"C:\media\" + fileName, "media", ItemAvailability.Available);
        return new ItemDetails(
            id, fileName, Path.GetExtension(fileName).TrimStart('.'), type, 1000, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MediaItemStatus.Indexed, null, location, null, null, null, []);
    }

    private KeyframeStripViewModel Create() => new(keyframes, null, clipboard, Localizer.Current) { OpenPlayer = () => opened++ };

    private KeyframeStripViewModel Loaded()
    {
        var strip = Create();
        strip.Show(Item(MediaType.Video, "a.mp4"));
        Pump.For(TimeSpan.FromMilliseconds(30));
        return strip;
    }

    private sealed class RecordingClipboard : ITextClipboard
    {
        public string? Text { get; private set; }

        public Exception? Failure { get; set; }

        public Task SetTextAsync(string text)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeyframes : ISender
    {
        public IReadOnlyList<KeyframeMarker> Result { get; set; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public List<Id<MediaItem>> Asked { get; } = [];

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetItemKeyframesQuery>(request);
            Asked.Add(query.ItemId);
            var result = Result;
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            return Failure is { } failure ? throw failure : (TResponse)(object)result;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
    }
}
