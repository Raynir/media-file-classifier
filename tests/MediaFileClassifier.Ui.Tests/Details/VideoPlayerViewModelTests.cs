using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Ui.Controls;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the preview's video player (E14-S06-T01): a video opens paused after the selection rests on it, the
/// controls drive the player, playback stops when the selection changes and pauses while hidden, volume and mute
/// carry over, and failures show why.
/// </summary>
public sealed class VideoPlayerViewModelTests
{
    private readonly FakePreviewPlayerFactory factory = new();
    private readonly FakeCapabilityGate gate = new();
    private readonly ShellLayout layout = new();
    private readonly FakeKeyframes keyframes = new();

    [Fact]
    public Task Show_AVideo_OpensItPausedAfterTheDelay() => TestApp.RunAsync(() =>
    {
        using var video = Create();

        video.Show(Item(MediaType.Video, "a.mp4"));

        Assert.True(video.IsActive);
        Assert.True(video.IsOpening);
        Assert.Empty(factory.Players);
        Settle(video);
        Assert.Equal([@"Open C:\media\a.mp4 00:00:00 paused"], factory.Player.Requests);
        Assert.Same(video.Frames, factory.Player.Sink);
        Assert.False(video.TogglePlayCommand.CanExecute(null));

        factory.Player.SetState(PlaybackState.Paused, TimeSpan.FromSeconds(90));
        Pump.Until(() => video.State == PlaybackState.Paused);

        Assert.True(video.IsReady);
        Assert.False(video.IsOpening);
        Assert.Equal(TimeSpan.FromSeconds(90), video.Duration);
        Assert.Equal("0:00 / 1:30", video.TimeText);
        Assert.True(video.TogglePlayCommand.CanExecute(null));
        Assert.Equal("Play", video.PlayText);
    });

    [Fact]
    public Task TogglePlay_PlaysAndPauses_AndThePositionFollowsThePlayer() => TestApp.RunAsync(() =>
    {
        using var video = Opened();
        var player = factory.Player;

        video.TogglePlayCommand.Execute(null);
        player.SetState(PlaybackState.Playing);
        Pump.Until(() => video.IsPlaying);
        player.SetPosition(TimeSpan.FromSeconds(12));
        Pump.Until(() => video.Position == TimeSpan.FromSeconds(12));
        video.TogglePlayCommand.Execute(null);

        Assert.Equal(["Play", "Pause"], player.Requests.Skip(1));
        Assert.Equal("0:12 / 1:30", video.TimeText);
        Assert.Equal("Pause", video.PlayText);
    });

    [Fact]
    public Task Seek_SeeksThePlayer_AndShowsTheTargetAtOnce() => TestApp.RunAsync(() =>
    {
        using var video = Opened();

        video.SeekCommand.Execute(TimeSpan.FromSeconds(42));

        Assert.Equal("Seek 00:00:42", factory.Player.Requests[^1]);
        Assert.Equal(TimeSpan.FromSeconds(42), video.Position);
    });

    [Fact]
    public Task Show_AnotherItem_StopsTheVideo_AndAnImageLeavesThePlayerInactive() => TestApp.RunAsync(() =>
    {
        using var video = Opened();

        video.Show(Item(MediaType.Image, "b.jpg"));

        Assert.Equal("Close", factory.Player.Requests[^1]);
        Assert.False(video.IsActive);
        Assert.Equal(PlaybackState.Closed, video.State);
        Pump.For(VideoPlayerViewModel.OpenDelay * 2);
        Assert.Single(factory.Players);
        Assert.DoesNotContain(factory.Player.Requests, request => request.Contains("b.jpg", StringComparison.Ordinal));

        video.Show(null);
        Assert.False(video.IsActive);
    });

    [Fact]
    public Task Show_SeveralVideosQuickly_OpensOnlyTheLast_WithOnePlayer() => TestApp.RunAsync(() =>
    {
        using var video = Create();

        video.Show(Item(MediaType.Video, "a.mp4"));
        video.Show(Item(MediaType.Video, "b.mp4"));
        video.Show(Item(MediaType.Video, "c.mp4"));
        Settle(video);
        video.Show(Item(MediaType.Video, "d.mp4"));
        Settle(video);

        Assert.Single(factory.Players);
        Assert.Equal(
            [@"Open C:\media\c.mp4 00:00:00 paused", "Close", @"Open C:\media\d.mp4 00:00:00 paused"],
            factory.Player.Requests);
    });

    [Fact]
    public Task Show_TheSameFileAgain_KeepsPlaying() => TestApp.RunAsync(() =>
    {
        using var video = Opened();
        var item = video.Item!;

        video.Show(item with { Title = "Renamed" });

        Assert.Equal("Renamed", video.Item!.Title);
        Assert.DoesNotContain("Close", factory.Player.Requests);
        Assert.Equal(PlaybackState.Paused, video.State);
    });

    [Theory]
    [InlineData(ItemAvailability.Offline)]
    [InlineData(ItemAvailability.Missing)]
    public Task Show_AVideoThatIsNotThere_LeavesItsThumbnail(ItemAvailability availability) => TestApp.RunAsync(() =>
    {
        using var video = Create();

        video.Show(Item(MediaType.Video, "a.mp4", availability));
        Pump.For(VideoPlayerViewModel.OpenDelay * 2);

        Assert.False(video.IsActive);
        Assert.Empty(factory.Players);
    });

    [Fact]
    public Task Show_WithoutLibVlc_SaysWhy_AndCreatesNoPlayer() => TestApp.RunAsync(() =>
    {
        gate.SetUnavailable(KnownCapabilities.VideoPlayback.Id, "Video playback unavailable: LibVLC failed to load.");
        using var video = Create();

        video.Show(Item(MediaType.Video, "a.mp4"));
        Pump.For(VideoPlayerViewModel.OpenDelay * 2);

        Assert.True(video.IsActive);
        Assert.Equal(PlaybackState.Failed, video.State);
        Assert.Equal("Video playback unavailable: LibVLC failed to load.", video.FailureText);
        Assert.Empty(factory.Players);
    });

    [Fact]
    public Task PlayerFailure_ShowsThatTheVideoCantPlayHere_UntilTheNextItem() => TestApp.RunAsync(() =>
    {
        using var video = Opened();

        factory.Player.SetState(PlaybackState.Failed, error: "No video frame within 5 seconds.");
        Pump.Until(() => video.State == PlaybackState.Failed);

        Assert.Equal("This video can't be played here.", video.FailureText);
        Assert.False(video.TogglePlayCommand.CanExecute(null));

        video.Show(Item(MediaType.Video, "b.mp4"));
        Assert.Null(video.FailureText);
    });

    [Fact]
    public Task VolumeAndMute_CarryOverToTheNextVideo() => TestApp.RunAsync(() =>
    {
        using var video = Create();
        video.Volume = 40;
        video.ToggleMuteCommand.Execute(null);

        video.Show(Item(MediaType.Video, "a.mp4"));
        Settle(video);

        Assert.Equal((40, true), (factory.Player.Volume, factory.Player.IsMuted));
        Assert.Equal("Unmute", video.MuteText);
        video.Volume = 75;
        video.ToggleMuteCommand.Execute(null);
        video.Show(Item(MediaType.Video, "b.mp4"));
        Settle(video);
        Assert.Equal((75, false), (factory.Player.Volume, factory.Player.IsMuted));
        Assert.Equal("Mute", video.MuteText);
    });

    [Fact]
    public Task HidingThePreviewOrThePanel_PausesPlayback() => TestApp.RunAsync(() =>
    {
        using var video = Opened();
        var player = factory.Player;
        player.SetState(PlaybackState.Playing);
        Pump.Until(() => video.IsPlaying);

        layout.IsDetailsPreviewCollapsed = true;
        Assert.Equal("Pause", player.Requests[^1]);

        layout.IsDetailsPreviewCollapsed = false;
        player.Requests.Clear();
        layout.Details.IsCollapsed = true;
        Assert.Equal(["Pause"], player.Requests);
    });

    [Fact]
    public Task Keyframes_BecomeTimelineMarkers_AndJumpingToOneSeeksToIt() => TestApp.RunAsync(() =>
    {
        var first = new KeyframeMarker(Id<Keyframe>.New(), 0, TimeSpan.FromSeconds(4));
        var second = new KeyframeMarker(Id<Keyframe>.New(), 1, TimeSpan.FromSeconds(31.5));
        keyframes.Result = [first, second];
        using var video = Opened();

        Assert.Equal([first, second], video.Keyframes);
        Assert.Equal([new TimelineMarker(first.Timestamp), new TimelineMarker(second.Timestamp)], video.Markers);
        Assert.Equal(video.Item!.Id, keyframes.Asked[^1]);

        video.JumpToKeyframeCommand.Execute(second);

        Assert.Equal("Seek 00:00:31.5000000", factory.Player.Requests[^1]);
        Assert.Equal(second.Timestamp, video.Position);
    });

    [Fact]
    public Task Keyframes_OfThePreviousVideo_AreClearedAtOnce_AndALateAnswerIsDropped() => TestApp.RunAsync(() =>
    {
        keyframes.Result = [new KeyframeMarker(Id<Keyframe>.New(), 0, TimeSpan.FromSeconds(4))];
        using var video = Opened();
        Assert.Single(video.Markers);

        keyframes.Gate = new TaskCompletionSource();
        video.Show(Item(MediaType.Video, "b.mp4"));
        Assert.Empty(video.Markers);
        Settle(video);
        video.Show(Item(MediaType.Image, "c.jpg"));
        keyframes.Gate.SetResult();
        Pump.For(TimeSpan.FromMilliseconds(50));

        Assert.Empty(video.Keyframes);
        Assert.Empty(video.Markers);
    });

    [Fact]
    public Task Keyframes_ThatFailToLoad_LeaveTheVideoPlayable() => TestApp.RunAsync(() =>
    {
        keyframes.Failure = new InvalidOperationException("The library closed.");
        using var video = Opened();

        Assert.Empty(video.Markers);
        Assert.True(video.TogglePlayCommand.CanExecute(null));
    });

    [Fact]
    public Task Dispose_DisposesThePlayer_AndLaterItemsDoNothing() => TestApp.RunAsync(() =>
    {
        var video = Opened();

        video.Dispose();
        video.Show(Item(MediaType.Video, "b.mp4"));
        Pump.For(VideoPlayerViewModel.OpenDelay * 2);

        Assert.True(factory.Player.IsDisposed);
        Assert.Single(factory.Players);
    });

    /// <summary>
    /// Answers the keyframes query, optionally after a gate opens or with a failure.
    /// </summary>
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

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ItemDetails Item(MediaType type, string fileName, ItemAvailability availability = ItemAvailability.Available)
    {
        var id = Id<MediaItem>.New();
        var path = @"C:\media\" + fileName;
        var location = new ItemLocation(id, new FileLocation(new FolderHandle(@"C:\media", "media"), fileName), path, "media", availability);
        return new ItemDetails(
            id, fileName, Path.GetExtension(fileName).TrimStart('.'), type, 1000, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MediaItemStatus.Indexed, null, location, null, null, null, []);
    }

    private static void Settle(VideoPlayerViewModel video) => Pump.For(VideoPlayerViewModel.OpenDelay * 2);

    private VideoPlayerViewModel Create() => new(factory, keyframes, gate, layout, Localizer.Current);

    private VideoPlayerViewModel Opened()
    {
        var video = Create();
        video.Show(Item(MediaType.Video, "a.mp4"));
        Settle(video);
        factory.Player.SetState(PlaybackState.Paused, TimeSpan.FromSeconds(90));
        Pump.Until(() => video.State == PlaybackState.Paused);
        return video;
    }
}
