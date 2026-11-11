using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The search box in the toolbar's Search slot (E09-S01-T05). Typing starts a search once the keystrokes pause for
/// <see cref="DefaultDelay"/> (D031); the search before it is cancelled. Enter searches at once, and Clear goes
/// back to browsing. "Find similar" (E09-S03-T02) shows as a chip with the item's thumbnail and name; text typed then
/// replaces it, and clearing the chip goes back to browsing. For an item without an embedding, the chip offers
/// "Analyze now" (E09-S03-T03) and shows how the analysis goes; the results appear by themselves once it's done.
/// </summary>
public sealed partial class SearchBarViewModel : ViewModelBase
{
    /// <summary>
    /// How long the text must rest before it is searched (D031).
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly SearchResultSource source;
    private readonly ThumbnailLoader? thumbnails;
    private readonly AnalyzeNow? analyzeNow;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? thumbnailLoading;
    private ThumbnailLease? thumbnailLease;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to search.</param>
    /// <param name="delay">The keystroke pause, or <see langword="null"/> for <see cref="DefaultDelay"/>.</param>
    /// <param name="thumbnails">Loads the chip's thumbnail, or <see langword="null"/> for none.</param>
    /// <param name="analyzeNow">Analyzes an item without an embedding, or <see langword="null"/> to not offer it.</param>
    public SearchBarViewModel(SearchResultSource source, TimeSpan? delay = null, ThumbnailLoader? thumbnails = null, AnalyzeNow? analyzeNow = null)
    {
        this.source = source;
        this.thumbnails = thumbnails;
        this.analyzeNow = analyzeNow;
        timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        timer.Tick += (_, _) => Submit();
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
    /// Searches for the text now, without waiting for the pause. Text replaces a "find similar" search.
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
    /// Empties the box and goes back to browsing; with an empty box, leaves similarity mode.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    public void Clear()
    {
        if (Text.Length == 0)
        {
            ClearSimilar();
            return;
        }

        Text = string.Empty;
        Submit();
    }

    partial void OnTextChanged(string value)
    {
        timer.Stop();
        timer.Start();
    }

    private bool CanClear() => Text.Length > 0 || SimilarTo is not null;

    // The source decides whether the search is a "find similar" one. Entering the mode empties the box.
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
