using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The search box in the toolbar's Search slot (E09-S01-T05). Typing starts a search once the keystrokes pause for
/// <see cref="DefaultDelay"/> (D031); the search before it is cancelled. Enter searches at once, and Clear goes
/// back to browsing. A picture picked with the image button, dropped on the box or pasted into it starts an
/// image-to-image search instead and shows as a chip (E09-S04-T01); typing replaces it. "Find similar" (E09-S03-T02)
/// shows as a chip with the item's thumbnail and name; text typed then replaces it, and clearing the chip goes back to
/// browsing. For an item without an embedding, the chip offers "Analyze now" (E09-S03-T03) and shows how the analysis
/// goes; the results appear by themselves once it's done.
/// </summary>
public sealed partial class SearchBarViewModel : ViewModelBase
{
    /// <summary>
    /// How long the text must rest before it is searched (D031).
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly SearchResultSource source;
    private readonly ISender sender;
    private readonly IReferenceImagePicker picker;
    private readonly Localizer localizer;
    private readonly ThumbnailLoader? thumbnails;
    private readonly AnalyzeNow? analyzeNow;
    private readonly ILogger logger;
    private readonly DispatcherTimer timer;
    private long lastKeystroke;
    private string lastTyped = string.Empty;
    private bool settingImage;
    private CancellationTokenSource? thumbnailLoading;
    private ThumbnailLease? thumbnailLease;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to search.</param>
    /// <param name="sender">Checks the reference pictures.</param>
    /// <param name="picker">Asks for a reference picture.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="delay">The keystroke pause, or <see langword="null"/> for <see cref="DefaultDelay"/>.</param>
    /// <param name="thumbnails">Loads the "find similar" chip's thumbnail, or <see langword="null"/> for none.</param>
    /// <param name="analyzeNow">Analyzes an item without an embedding, or <see langword="null"/> to not offer it.</param>
    /// <param name="logger">Logger; it gets the time from the last keystroke to the results (E09-S02-T03).</param>
    public SearchBarViewModel(
        SearchResultSource source,
        ISender sender,
        IReferenceImagePicker picker,
        Localizer localizer,
        TimeSpan? delay = null,
        ThumbnailLoader? thumbnails = null,
        AnalyzeNow? analyzeNow = null,
        ILogger<SearchBarViewModel>? logger = null)
    {
        this.logger = logger ?? NullLogger<SearchBarViewModel>.Instance;
        this.source = source;
        this.sender = sender;
        this.picker = picker;
        this.localizer = localizer;
        this.thumbnails = thumbnails;
        this.analyzeNow = analyzeNow;
        timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        timer.Tick += (_, _) => Submit();
        source.SummaryChanged += (_, _) => LogLatency();
        source.Changed += (_, _) =>
        {
            // "Find similar" starts from a command, and the results follow the browse scope on any thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Sync();
            }
            else
            {
                Dispatcher.UIThread.Post(Sync);
            }
        };
        if (analyzeNow is not null)
        {
            AnalyzeState = analyzeNow.State;
            analyzeNow.StateChanged += (_, _) => Dispatcher.UIThread.Post(() => AnalyzeState = analyzeNow.State);
        }
    }

    /// <summary>
    /// Gets or sets what the user typed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets the item of the "find similar" search, or <see langword="null"/> outside similarity mode.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimilarMode), nameof(SimilarLabel))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial SimilarSource? SimilarTo { get; private set; }

    /// <summary>
    /// Gets the thumbnail of <see cref="SimilarTo"/>, or <see langword="null"/> while it loads or without one.
    /// </summary>
    [ObservableProperty]
    public partial IImage? SimilarThumbnail { get; private set; }

    /// <summary>
    /// Gets where "Analyze now" stands for <see cref="SimilarTo"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnalyzeNowOffered), nameof(AnalyzeStatus), nameof(HasAnalyzeStatus))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeNowCommand))]
    public partial AnalyzeNowState AnalyzeState { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the chip offers "Analyze now": the item has no embedding, and isn't being
    /// analyzed.
    /// </summary>
    public bool IsAnalyzeNowOffered => AnalyzeState is AnalyzeNowState.Offered or AnalyzeNowState.Failed or AnalyzeNowState.NotRunning;

    /// <summary>
    /// Gets how "Analyze now" goes, e.g. <c>Analyzing…</c>, or <see langword="null"/> when there's nothing to say.
    /// </summary>
    public string? AnalyzeStatus => AnalyzeState switch
    {
        AnalyzeNowState.Analyzing => Localizer.Current["Search_Analyzing"],
        AnalyzeNowState.Paused => Localizer.Current["Search_AnalyzePaused"],
        AnalyzeNowState.NotRunning => Localizer.Current["Search_AnalyzeNotRunning"],
        AnalyzeNowState.Failed => Localizer.Current["Search_AnalyzeFailed"],
        _ => null,
    };

    /// <summary>
    /// Gets a value indicating whether <see cref="AnalyzeStatus"/> is shown.
    /// </summary>
    public bool HasAnalyzeStatus => AnalyzeStatus is not null;

    /// <summary>
    /// Gets a value indicating whether the chip is shown.
    /// </summary>
    public bool IsSimilarMode => SimilarTo is not null;

    /// <summary>
    /// Gets the chip's text, e.g. <c>Similar to: IMG_1234.jpg</c>, or <see langword="null"/> outside similarity mode.
    /// </summary>
    public string? SimilarLabel => SimilarTo is { } similar ? Localizer.Current.Format("Search_SimilarTo", similar.Name) : null;

    /// <summary>
    /// Gets the picture being searched with, shown as the chip, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferenceImage))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial ReferenceImage? ReferenceImage { get; private set; }

    /// <summary>
    /// Gets why the last picture couldn't be used, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImageError))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string? ImageError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the chip shows.
    /// </summary>
    public bool HasReferenceImage => ReferenceImage is not null;

    /// <summary>
    /// Gets a value indicating whether the picture error shows.
    /// </summary>
    public bool HasImageError => ImageError is not null;

    /// <summary>
    /// Searches for the text now, without waiting for the pause. Text replaces a picture or a "find similar" search.
    /// </summary>
    [RelayCommand]
    public void Submit()
    {
        timer.Stop();
        source.SetText(Text);
        Sync();
    }

    /// <summary>
    /// Queues the item of the chip for analysis at user priority; the results appear once its embedding is ready.
    /// </summary>
    /// <returns>A task that completes when the item is queued.</returns>
    [RelayCommand(CanExecute = nameof(IsAnalyzeNowOffered))]
    public Task AnalyzeNowAsync() => analyzeNow?.AnalyzeAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Leaves similarity mode and goes back to browsing.
    /// </summary>
    [RelayCommand]
    public void ClearSimilar()
    {
        source.ClearSimilar();
        Sync();
    }

    /// <summary>
    /// Empties the box, removes the picture and goes back to browsing; with an empty box, leaves similarity mode.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    public void Clear()
    {
        ImageError = null;
        if (ReferenceImage is not null)
        {
            RemoveImage();
        }

        if (Text.Length == 0 && SimilarTo is not null)
        {
            ClearSimilar();
            return;
        }

        Text = string.Empty;
        Submit();
    }

    /// <summary>
    /// Removes the picture and goes back to browsing.
    /// </summary>
    [RelayCommand]
    public void RemoveImage()
    {
        ReferenceImage = null;
        source.SetReferenceImage(null);
    }

    /// <summary>
    /// Asks for a picture and searches with it.
    /// </summary>
    /// <returns>A task that completes when the search has started, or the picture was refused.</returns>
    [RelayCommand]
    public async Task PickImageAsync()
    {
        PickedImage? picked;
        try
        {
            picked = await picker.PickAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ImageError = localizer.Get("Search_ImageError_Unreadable");
            return;
        }

        if (picked is not null)
        {
            await UseImageAsync(picked);
        }
    }

    /// <summary>
    /// Searches with a picture that was picked, dropped or pasted, once it's checked. A picture that isn't one of the
    /// supported images shows an error and leaves the search as it is.
    /// </summary>
    /// <param name="picture">The picture.</param>
    /// <returns>A task that completes when the search has started, or the picture was refused.</returns>
    public async Task UseImageAsync(PickedImage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        if (picture.IsTooLarge)
        {
            ImageError = localizer.Get("Search_ImageError_TooLarge");
            return;
        }

        var typed = Text;
        var load = await sender.Send(new LoadReferenceImageQuery(picture.Content, picture.Name));

        // The user typed while the picture was checked: the newer text wins.
        if (Text != typed)
        {
            return;
        }

        if (load.Image is not { } image)
        {
            ImageError = localizer.Get(load.Problem switch
            {
                ReferenceImageProblem.TooLarge => "Search_ImageError_TooLarge",
                ReferenceImageProblem.Unreadable => "Search_ImageError_Unreadable",
                _ => "Search_ImageError_Unsupported",
            });
            return;
        }

        // The picture replaces the text: emptying the box must not start a text search of its own.
        timer.Stop();
        settingImage = true;
        try
        {
            Text = string.Empty;
        }
        finally
        {
            settingImage = false;
        }

        ImageError = null;
        ReferenceImage = image;
        source.SetReferenceImage(image);
    }

    /// <summary>
    /// Shows that a picture couldn't be read, for example from a drop.
    /// </summary>
    public void ShowUnreadableImage() => ImageError = localizer.Get("Search_ImageError_Unreadable");

    partial void OnTextChanged(string value)
    {
        if (settingImage)
        {
            return;
        }

        ImageError = null;

        // Typing replaces the picture at once, so the chip and the results agree; the text search follows after the pause.
        if (value.Length > 0 && ReferenceImage is not null)
        {
            RemoveImage();
        }

        lastTyped = value;
        lastKeystroke = Stopwatch.GetTimestamp();
        timer.Stop();
        timer.Start();
    }

    // The time from the last keystroke to the results being known: the pause, the search and the hand-over to this
    // thread. Logged once per typed text, so the real numbers can be compared with the budget of D031.
    private void LogLatency()
    {
        // Only the summary of the text that was typed counts: another search's summary says nothing about typing.
        if (source.Summary is not { } summary || summary.Text.Length == 0 || summary.Text != lastTyped)
        {
            return;
        }

        var typedAt = Interlocked.Exchange(ref lastKeystroke, 0);
        if (typedAt != 0)
        {
            LogKeystrokeToResults(logger, Stopwatch.GetElapsedTime(typedAt).TotalMilliseconds, summary.Mode, summary.Semantic, summary.TotalCount);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Search as you type: {Milliseconds:F0} ms from the last keystroke to the results ({Mode}, semantic {Semantic}, {Total} results)")]
    private static partial void LogKeystrokeToResults(ILogger logger, double milliseconds, SearchMode mode, SemanticSearchStatus semantic, int total);

    private bool CanClear() => Text.Length > 0 || ReferenceImage is not null || ImageError is not null || SimilarTo is not null;

    // The source decides whether the search is a "find similar" one. Entering the mode empties the box and removes
    // the picture.
    private void Sync()
    {
        var similar = source.SimilarTo;
        if (SimilarTo == similar)
        {
            return;
        }

        SimilarTo = similar;
        thumbnailLoading?.Cancel();
        thumbnailLoading = null;
        SimilarThumbnail = null;
        thumbnailLease?.Dispose();
        thumbnailLease = null;
        if (similar is null)
        {
            return;
        }

        // "Find similar" replaces a picture.
        ReferenceImage = null;
        ImageError = null;
        Text = string.Empty;
        timer.Stop();
        if (thumbnails is not null)
        {
            _ = LoadThumbnailAsync(thumbnails, similar);
        }
    }

    private async Task LoadThumbnailAsync(ThumbnailLoader loader, SimilarSource item)
    {
        var cancellation = new CancellationTokenSource();
        thumbnailLoading = cancellation;
        ThumbnailLease? lease;
        try
        {
            lease = await loader.LoadAsync(item.ItemId, item.FileModifiedAt, ThumbnailSize.Small, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(thumbnailLoading, cancellation) || SimilarTo != item)
        {
            lease?.Dispose();
            return;
        }

        thumbnailLoading = null;
        thumbnailLease = lease;
        SimilarThumbnail = lease?.Bitmap;
    }
}
