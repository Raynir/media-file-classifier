using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The keyframe strip under the preview of a video or an animated image (E14-S13-T01), and the moment chosen in it.
/// Choosing a frame raises <see cref="Chosen"/>, where the panel seeks the in-app player. With no in-app playback
/// (D026) the moment is only selected: "Open in player" then opens the file and copies the time (E14-S13-T02).
/// </summary>
public sealed partial class KeyframeStripViewModel : ViewModelBase, IDisposable
{
    private readonly ISender? sender;
    private readonly KeyframePictureLoader? pictures;
    private readonly ITextClipboard? clipboard;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private CancellationTokenSource? loading;
    private int loads;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyframeStripViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the keyframes query, or <see langword="null"/> for an empty strip.</param>
    /// <param name="pictures">Loads the keyframes' pictures, or <see langword="null"/> to show times only.</param>
    /// <param name="clipboard">Takes the chosen time when the file opens in the default app.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logs keyframes that couldn't be loaded.</param>
    public KeyframeStripViewModel(
        ISender? sender,
        KeyframePictureLoader? pictures,
        ITextClipboard? clipboard,
        Localizer localizer,
        ILogger<KeyframeStripViewModel>? logger = null)
    {
        this.sender = sender;
        this.pictures = pictures;
        this.clipboard = clipboard;
        this.localizer = localizer;
        this.logger = logger ?? NullLogger<KeyframeStripViewModel>.Instance;
    }

    /// <summary>
    /// Raised on the UI thread when the user chooses a keyframe, with its time.
    /// </summary>
    public event EventHandler<TimeSpan>? Chosen;

    /// <summary>
    /// Gets or sets what opens the file in the default app (the item's open command). Set by the panel.
    /// </summary>
    public Action? OpenPlayer { get; set; }

    /// <summary>
    /// Gets a value indicating whether the strip is shown: for a video or an animated image.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsVisible { get; private set; }

    /// <summary>
    /// Gets the keyframes, in time order.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyframes), nameof(IsEmpty))]
    public partial IReadOnlyList<KeyframeViewModel> Keyframes { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether there are keyframes to show.
    /// </summary>
    public bool HasKeyframes => Keyframes.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the strip is shown with nothing in it: not analysed yet, or loading.
    /// </summary>
    public bool IsEmpty => IsVisible && !HasKeyframes;

    /// <summary>
    /// Gets or sets a value indicating whether the item can't play in the app, so choosing only selects a moment and
    /// "Open in player" is offered (D026). Set by the panel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenPlayer))]
    public partial bool IsFallback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file can be opened: it's there. Set by the panel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenPlayer))]
    public partial bool IsFileAvailable { get; set; }

    /// <summary>
    /// Gets a value indicating whether "Open in player" shows.
    /// </summary>
    public bool CanOpenPlayer => IsVisible && IsFallback && IsFileAvailable;

    /// <summary>
    /// Gets the chosen moment, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MomentText))]
    public partial TimeSpan? Moment { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the chosen time is on the clipboard.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MomentText))]
    public partial bool IsMomentCopied { get; private set; }

    /// <summary>
    /// Gets what the preview says about the chosen moment, e.g. "Selected moment: 02:37", then that it was copied;
    /// <see langword="null"/> without one. Only the fallback shows it: in-app playback jumps there instead.
    /// </summary>
    public string? MomentText => Moment is not { } moment || !IsFallback
        ? null
        : localizer.Format(IsMomentCopied ? "Keyframes_MomentCopied" : "Keyframes_MomentSelected", Format.Timestamp(moment));

    /// <summary>
    /// Shows an item's strip: for a video or an animated image the keyframes load; for anything else it hides.
    /// </summary>
    /// <param name="item">The item, or <see langword="null"/> for none.</param>
    public void Show(ItemDetails? item)
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        ++loads;
        Clear();
        Moment = null;
        IsMomentCopied = false;
        IsVisible = item is { MediaType: MediaType.Video or MediaType.AnimatedImage };
        OpenInPlayerCommand.NotifyCanExecuteChanged();
        if (!IsVisible || sender is null)
        {
            return;
        }

        loading = new CancellationTokenSource();
        _ = LoadAsync(item!, loads, loading.Token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        Clear();
    }

    /// <summary>
    /// Chooses a moment, as clicking its keyframe does.
    /// </summary>
    /// <param name="keyframe">The keyframe.</param>
    [RelayCommand]
    public void Choose(KeyframeViewModel keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);

        foreach (var other in Keyframes)
        {
            other.IsSelected = ReferenceEquals(other, keyframe);
        }

        IsMomentCopied = false;
        Moment = keyframe.Timestamp;
        Chosen?.Invoke(this, keyframe.Timestamp);
    }

    /// <summary>
    /// Opens the file in the default app, and copies the chosen time to the clipboard when there is one (D026).
    /// </summary>
    /// <returns>A task that completes when the time is copied.</returns>
    [RelayCommand(CanExecute = nameof(CanOpenPlayer))]
    public async Task OpenInPlayerAsync()
    {
        OpenPlayer?.Invoke();
        if (Moment is { } moment && clipboard is not null)
        {
            try
            {
                await clipboard.SetTextAsync(Format.Timestamp(moment));
                IsMomentCopied = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // No clipboard (e.g. no window): the time still shows.
                LogCopyFailed(logger, ex);
            }
        }
    }

    partial void OnIsFallbackChanged(bool value)
    {
        OnPropertyChanged(nameof(MomentText));
        OpenInPlayerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFileAvailableChanged(bool value) => OpenInPlayerCommand.NotifyCanExecuteChanged();

    private async Task LoadAsync(ItemDetails item, int load, CancellationToken token)
    {
        try
        {
            var markers = await sender!.Send(new GetItemKeyframesQuery(item.Id), token);
            if (load != loads)
            {
                return;
            }

            var keyframes = markers.Select(marker => new KeyframeViewModel(marker, localizer)).ToList();
            Keyframes = keyframes;
            if (pictures is null)
            {
                return;
            }

            foreach (var keyframe in keyframes)
            {
                var lease = await pictures.LoadAsync(item.Id, keyframe.Index, token);
                if (load != loads)
                {
                    lease?.Dispose();
                    return;
                }

                keyframe.SetPicture(lease);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another item took over.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The strip stays empty or without pictures; the preview works without it.
            LogLoadFailed(logger, ex);
        }
    }

    private void Clear()
    {
        var old = Keyframes;
        Keyframes = [];
        foreach (var keyframe in old)
        {
            keyframe.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't load the keyframe strip")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't copy the chosen moment to the clipboard")]
    private static partial void LogCopyFailed(ILogger logger, Exception exception);
}

/// <summary>
/// One frame of the keyframe strip.
/// </summary>
public sealed partial class KeyframeViewModel : ViewModelBase, IDisposable
{
    private readonly Localizer localizer;
    private ThumbnailLease? lease;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyframeViewModel"/> class.
    /// </summary>
    /// <param name="marker">The keyframe.</param>
    /// <param name="localizer">UI strings.</param>
    public KeyframeViewModel(KeyframeMarker marker, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(marker);

        Marker = marker;
        this.localizer = localizer;
    }

    /// <summary>
    /// Gets the keyframe.
    /// </summary>
    public KeyframeMarker Marker { get; }

    /// <summary>
    /// Gets the keyframe's index among the item's keyframes.
    /// </summary>
    public int Index => Marker.Index;

    /// <summary>
    /// Gets where the keyframe is in the video.
    /// </summary>
    public TimeSpan Timestamp => Marker.Timestamp;

    /// <summary>
    /// Gets the time to show under the picture, e.g. <c>02:37</c>.
    /// </summary>
    public string TimeText => Format.Timestamp(Timestamp);

    /// <summary>
    /// Gets the keyframe's name for screen readers, e.g. "Keyframe at 02:37".
    /// </summary>
    public string Name => localizer.Format("Keyframes_Frame", TimeText);

    /// <summary>
    /// Gets the keyframe's picture, or <see langword="null"/> when there is none.
    /// </summary>
    [ObservableProperty]
    public partial Bitmap? Picture { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the chosen moment.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Gives the keyframe its picture.
    /// </summary>
    /// <param name="newLease">The lease on the picture, or <see langword="null"/>.</param>
    public void SetPicture(ThumbnailLease? newLease)
    {
        lease?.Dispose();
        lease = newLease;
        Picture = newLease?.Bitmap;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Picture = null;
        lease?.Dispose();
        lease = null;
    }
}
