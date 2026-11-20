using Avalonia.Labs.AnimatedImage;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The animated GIF or WebP in the details panel preview (E14-S12-T01). While it plays, <c>AnimatedImage</c> draws
/// <see cref="PlaybackSource"/> with the frames' own delays. The control cannot say or set its frame, so this view
/// model keeps the same clock: pausing shows <see cref="CurrentFrame"/> as a plain bitmap, and stepping moves it a
/// frame back or forward. Playing again starts a new playback source at the shown frame, so the loop carries on from
/// there. Use it on the UI thread; <see cref="Dispose"/> stops playback and lets the frames go.
/// </summary>
public sealed partial class AnimatedPreviewViewModel : ViewModelBase, IDisposable
{
    private readonly IAnimatedBitmap animation;
    private readonly AnimationTimeline timeline;
    private readonly TimeProvider clock;
    private readonly Localizer localizer;
    private long playingSince;
    private TimeSpan playingFrom;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimatedPreviewViewModel"/> class. The animation starts playing.
    /// </summary>
    /// <param name="animation">The loaded animation, which this view model owns.</param>
    /// <param name="clock">The clock that times the frames.</param>
    /// <param name="localizer">UI strings.</param>
    public AnimatedPreviewViewModel(IAnimatedBitmap animation, TimeProvider clock, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(animation);
        this.animation = animation;
        this.clock = clock;
        this.localizer = localizer;
        timeline = new AnimationTimeline(animation.Delays);
        CurrentFrame = animation.Frames.Count > 0 ? animation.Frames[0] : null;
        PlaybackSource = animation;
        Play();
    }

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public int FrameCount => timeline.FrameCount;

    /// <summary>
    /// Gets a value indicating whether the animation is playing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    public partial bool IsPlaying { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the animation is paused, so <see cref="CurrentFrame"/> shows.
    /// </summary>
    public bool IsPaused => !IsPlaying;

    /// <summary>
    /// Gets the animation the control plays; a new one each time playback starts, so it starts at the shown frame.
    /// </summary>
    [ObservableProperty]
    public partial IAnimatedBitmap? PlaybackSource { get; private set; }

    /// <summary>
    /// Gets the paused frame, or the first frame before playback has shown more.
    /// </summary>
    [ObservableProperty]
    public partial Bitmap? CurrentFrame { get; private set; }

    /// <summary>
    /// Gets the shown frame's index, from 0, as of the last pause or step.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameText))]
    public partial int FrameIndex { get; private set; }

    /// <summary>
    /// Gets "Frame 3 of 12", as of the last pause or step.
    /// </summary>
    public string FrameText => localizer.Format("Details_Preview_Frame", FrameIndex + 1, FrameCount);

    /// <summary>
    /// Plays or pauses.
    /// </summary>
    [RelayCommand]
    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>
    /// Starts playing from the shown frame.
    /// </summary>
    public void Play()
    {
        if (IsPlaying || timeline.FrameCount == 0)
        {
            return;
        }

        playingFrom = timeline.StartOf(FrameIndex);
        playingSince = clock.GetTimestamp();
        PlaybackSource = FrameIndex == 0 ? animation : RotatedFrom(FrameIndex);
        IsPlaying = true;
    }

    /// <summary>
    /// Pauses on the frame that is showing.
    /// </summary>
    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        ShowFrame(timeline.IndexAt(playingFrom + clock.GetElapsedTime(playingSince)));
    }

    /// <summary>
    /// Pauses, if needed, and shows the next frame, after the last one the first.
    /// </summary>
    [RelayCommand]
    public void StepForward()
    {
        Pause();
        ShowFrame((FrameIndex + 1) % Math.Max(1, FrameCount));
    }

    /// <summary>
    /// Pauses, if needed, and shows the previous frame, before the first one the last.
    /// </summary>
    [RelayCommand]
    public void StepBack()
    {
        Pause();
        ShowFrame((FrameIndex + Math.Max(1, FrameCount) - 1) % Math.Max(1, FrameCount));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IsPlaying = false;
        PlaybackSource = null;
        CurrentFrame = null;
        (animation as IDisposable)?.Dispose();
    }

    private void ShowFrame(int index)
    {
        FrameIndex = index;
        CurrentFrame = index < animation.Frames.Count ? animation.Frames[index] : null;
    }

    private IAnimatedBitmap RotatedFrom(int index)
    {
        // The same bitmaps in a new order: no copy, and the wrapper is never disposed, so the frames stay ours.
        var frames = new Bitmap[timeline.FrameCount];
        var delays = new int[timeline.FrameCount];
        for (var i = 0; i < frames.Length; i++)
        {
            var from = (index + i) % frames.Length;
            frames[i] = animation.Frames[from];
            delays[i] = animation.Delays[from];
        }

        return IAnimatedBitmap.Load(frames, delays);
    }
}
