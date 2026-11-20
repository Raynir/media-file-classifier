using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Playback;
using MediaFileClassifier.Ui.Controls;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The video player in the preview (E14-S06): play and pause, the timeline, volume and mute, and the time. A video
/// opens paused on its first frame once the selection has rested on it for <see cref="OpenDelay"/>, so arrowing
/// through results neither decodes nor makes a sound. Playback stops when the selection changes, and pauses while
/// the preview is collapsed, the panel is hidden or the window is minimized. Volume and mute carry over from one
/// video to the next while the app runs. The timeline shows the video's keyframes (E14-S06-T02); MFC-119's keyframe
/// strip reuses <see cref="Keyframes"/> and <see cref="JumpToKeyframeCommand"/>. Use it on the UI thread.
/// </summary>
public sealed partial class VideoPlayerViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The largest frame the player decodes to: the preview size the playback spike measured (D070).
    /// </summary>
    internal const int MaxFrameWidth = 1280;

    /// <summary>
    /// The largest frame height, with <see cref="MaxFrameWidth"/>.
    /// </summary>
    internal const int MaxFrameHeight = 720;

    /// <summary>
    /// How long the selection must rest on a video before it opens, as for an image's original.
    /// </summary>
    internal static readonly TimeSpan OpenDelay = DetailsPanelViewModel.OriginalDelay;

    private readonly IMediaPreviewPlayerFactory? factory;
    private readonly ISender? sender;
    private readonly ICapabilityGate? capabilities;
    private readonly ShellLayout? layout;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private IMediaPreviewPlayer? player;
    private CancellationTokenSource? opening;
    private string? openPath;
    private TimeSpan? pendingJump;
    private int pendingRefresh;
    private int keyframeLoads;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoPlayerViewModel"/> class.
    /// </summary>
    /// <param name="factory">Creates the player, or <see langword="null"/> when this platform has no in-app playback.</param>
    /// <param name="sender">Gets the keyframes, or <see langword="null"/> for no markers.</param>
    /// <param name="capabilities">Tells whether LibVLC loaded, or <see langword="null"/> to assume it did.</param>
    /// <param name="layout">The shell layout, to pause while the preview or the panel is hidden; or <see langword="null"/>.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logs playback failures, or <see langword="null"/> for none.</param>
    public VideoPlayerViewModel(
        IMediaPreviewPlayerFactory? factory,
        ISender? sender,
        ICapabilityGate? capabilities,
        ShellLayout? layout,
        Localizer localizer,
        ILogger<VideoPlayerViewModel>? logger = null)
    {
        this.factory = factory;
        this.sender = sender;
        this.capabilities = capabilities;
        this.layout = layout;
        this.localizer = localizer;
        this.logger = logger ?? NullLogger<VideoPlayerViewModel>.Instance;
        if (layout is not null)
        {
            layout.PropertyChanged += OnLayoutChanged;
            layout.Details.PropertyChanged += OnLayoutChanged;
        }
    }

    /// <summary>
    /// Gets the frames the view draws.
    /// </summary>
    public VideoFrameBuffer Frames { get; } = new();

    /// <summary>
    /// Gets a value indicating whether a video is shown in the player, so the preview shows its controls instead of
    /// the zoom buttons.
    /// </summary>
    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    /// <summary>
    /// Gets the player's state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(IsReady), nameof(IsOpening))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayCommand), nameof(SeekCommand), nameof(JumpToKeyframeCommand))]
    public partial PlaybackState State { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the video plays.
    /// </summary>
    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>
    /// Gets a value indicating whether a frame is shown and the controls work.
    /// </summary>
    public bool IsReady => State is PlaybackState.Paused or PlaybackState.Playing or PlaybackState.Ended;

    /// <summary>
    /// Gets a value indicating whether the video is still opening.
    /// </summary>
    public bool IsOpening => IsActive && State == PlaybackState.Opening;

    /// <summary>
    /// Gets the playback position.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial TimeSpan Position { get; private set; }

    /// <summary>
    /// Gets the video's length.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial TimeSpan Duration { get; private set; }

    /// <summary>
    /// Gets the position and the length, e.g. <c>0:12 / 1:30</c>.
    /// </summary>
    public string TimeText => localizer.Format("Video_Time", Format.Duration(Position), Format.Duration(Duration));

    /// <summary>
    /// Gets or sets the volume, from 0 to 100. It carries over to the next video.
    /// </summary>
    [ObservableProperty]
    public partial double Volume { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether the sound is muted. It carries over to the next video.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteText))]
    public partial bool IsMuted { get; set; }

    /// <summary>
    /// Gets the mute button's name: what it does.
    /// </summary>
    public string MuteText => localizer.Get(IsMuted ? "Video_Unmute" : "Video_Mute");

    /// <summary>
    /// Gets the play button's name: what it does.
    /// </summary>
    public string PlayText => localizer.Get(IsPlaying ? "Video_Pause" : "Video_Play");

    /// <summary>
    /// Gets why the video can't play here, or <see langword="null"/>. MFC-119 adds Open in default app next to it
    /// (D026).
    /// </summary>
    [ObservableProperty]
    public partial string? FailureText { get; private set; }

    /// <summary>
    /// Gets the shown video's keyframes (E06-S02), in time order; empty until they're loaded or when it has none.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<KeyframeMarker> Keyframes { get; private set; } = [];

    /// <summary>
    /// Gets the timeline's markers: the keyframes now, matched moments later (E09-S06).
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TimelineMarker> Markers { get; private set; } = [];

    /// <summary>
    /// Gets the item shown, or <see langword="null"/>.
    /// </summary>
    public ItemDetails? Item { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an item can play in the preview: a video whose file is there.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>Whether it can play.</returns>
    public static bool CanPlay(ItemDetails? item) =>
        item is { MediaType: MediaType.Video, Location: { Availability: ItemAvailability.Available, FullPath: not null } };

    /// <summary>
    /// Shows an item: a playable video opens paused on its first frame after <see cref="OpenDelay"/>; anything else
    /// stops and closes the player.
    /// </summary>
    /// <param name="item">The item, or <see langword="null"/> for none.</param>
    public void Show(ItemDetails? item)
    {
        if (disposed)
        {
            return;
        }

        var path = CanPlay(item) ? item!.Location.FullPath : null;
        if (path is not null && path == openPath && Item?.FileModifiedAt == item!.FileModifiedAt)
        {
            // The same file again, e.g. the details refreshed: keep playing.
            Item = item;
            return;
        }

        CancelOpening();
        Item = item;
        openPath = null;
        pendingJump = null;
        FailureText = null;
        ++keyframeLoads;
        Keyframes = [];
        Markers = [];
        player?.Close();
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        State = PlaybackState.Closed;
        IsActive = path is not null;
        OnPropertyChanged(nameof(IsOpening));
        if (path is null)
        {
            return;
        }

        if (factory is null || capabilities?.Check(KnownCapabilities.VideoPlayback.Id) is { IsAvailable: false })
        {
            FailureText = capabilities?.Check(KnownCapabilities.VideoPlayback.Id).Reason ?? localizer.Get("Video_Failed");
            State = PlaybackState.Failed;
            return;
        }

        State = PlaybackState.Opening;
        opening = new CancellationTokenSource();
        _ = OpenAfterDelayAsync(path, opening.Token);
    }

    /// <summary>
    /// Pauses, e.g. when the preview is hidden.
    /// </summary>
    public void Pause()
    {
        // Always forwarded: State lags the player, e.g. a video reopening at the end to play is still Opening here.
        // The player ignores a pause when nothing plays.
        if (openPath is not null)
        {
            player?.Pause();
        }
    }

    /// <summary>
    /// Opens the shown video at a time and keeps whether it plays ("jump to the moment"); for MFC-119's keyframe
    /// strip and search matches.
    /// </summary>
    /// <param name="time">The time.</param>
    public void JumpTo(TimeSpan time)
    {
        if (!IsActive || State == PlaybackState.Failed)
        {
            return;
        }

        time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        if (player is null || openPath is null)
        {
            // Still waiting for OpenDelay: the video opens at the time.
            if (opening is not null)
            {
                pendingJump = time;
                Position = time;
            }

            return;
        }

        if (!IsReady)
        {
            // The player may not have the file yet, so a seek would be lost: open it again at the time.
            player.Open(openPath, time, play: false);
            Position = time;
            return;
        }

        player.Seek(time);
        Position = player.Position;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelOpening();
        if (layout is not null)
        {
            layout.PropertyChanged -= OnLayoutChanged;
            layout.Details.PropertyChanged -= OnLayoutChanged;
        }

        if (player is not null)
        {
            player.StateChanged -= OnPlayerChanged;
            player.PositionChanged -= OnPlayerChanged;
            player.Dispose();
            player = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void TogglePlay()
    {
        if (player is null)
        {
            return;
        }

        if (IsPlaying)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void JumpToKeyframe(KeyframeMarker keyframe) => JumpTo(keyframe.Timestamp);

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void Seek(TimeSpan time) => JumpTo(time);

    partial void OnVolumeChanged(double value)
    {
        if (player is not null)
        {
            player.Volume = (int)Math.Round(value);
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (player is not null)
        {
            player.IsMuted = value;
        }
    }

    partial void OnStateChanged(PlaybackState value) => OnPropertyChanged(nameof(PlayText));

    private async Task OpenAfterDelayAsync(string path, CancellationToken token)
    {
        try
        {
            await Task.Delay(OpenDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || !EnsurePlayer())
        {
            return;
        }

        openPath = path;
        player!.Open(path, pendingJump ?? TimeSpan.Zero, play: false);
        pendingJump = null;
        if (Item is { } item)
        {
            await LoadKeyframesAsync(item.Id, token);
        }
    }

    private async Task LoadKeyframesAsync(Id<MediaItem> itemId, CancellationToken token)
    {
        if (sender is null)
        {
            return;
        }

        var load = ++keyframeLoads;
        try
        {
            var keyframes = await sender.Send(new GetItemKeyframesQuery(itemId), token);
            if (load != keyframeLoads)
            {
                return;
            }

            Keyframes = keyframes;
            Markers = [.. keyframes.Select(keyframe => new TimelineMarker(keyframe.Timestamp))];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another item took over.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The video still plays, just without markers.
            LogKeyframesFailed(logger, ex);
        }
    }

    private bool EnsurePlayer()
    {
        if (player is not null)
        {
            return true;
        }

        try
        {
            player = factory!.Create(Frames, MaxFrameWidth, MaxFrameHeight);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // LibVLC loaded at startup but can't make a player now: this item, and the next ones, fall back to D026.
            LogPlayerFailed(logger, ex);
            FailureText = localizer.Get("Video_Failed");
            State = PlaybackState.Failed;
            return false;
        }

        player.Volume = (int)Math.Round(Volume);
        player.IsMuted = IsMuted;
        player.StateChanged += OnPlayerChanged;
        player.PositionChanged += OnPlayerChanged;
        return true;
    }

    private void CancelOpening()
    {
        opening?.Cancel();
        opening?.Dispose();
        opening = null;
    }

    // On the player's threads: one refresh waits on the UI thread at a time.
    private void OnPlayerChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref pendingRefresh, 1) == 0)
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref pendingRefresh, 0);
        if (player is null || openPath is null || disposed)
        {
            return;
        }

        var state = player.State;
        if (state == PlaybackState.Closed)
        {
            // The close of the previous file, or a request still queued: the player catches up.
            return;
        }

        State = state;
        Duration = player.Duration;
        Position = player.Position;
        if (state == PlaybackState.Failed && FailureText is null)
        {
            LogPlaybackFailed(logger, Item?.FileName, player.Error);
            FailureText = localizer.Get("Video_Failed");
        }
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellLayout.IsDetailsPreviewCollapsed) or nameof(SidePanel.IsExpanded)
            && layout is not null && (layout.IsDetailsPreviewCollapsed || !layout.Details.IsExpanded))
        {
            Pause();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the keyframes for the timeline")]
    private static partial void LogKeyframesFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't create the video player; videos open in the default app")]
    private static partial void LogPlayerFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't play {FileName} in the preview: {Error}")]
    private static partial void LogPlaybackFailed(ILogger logger, string? fileName, string? error);
}
