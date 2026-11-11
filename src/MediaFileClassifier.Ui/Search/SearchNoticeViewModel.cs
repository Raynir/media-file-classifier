using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The notice bar above the results while a search is on (E09-S01-T05): how many results, a hint when the semantic
/// search could not run, and the "Show N less relevant results" option.
/// </summary>
public sealed partial class SearchNoticeViewModel : ViewModelBase
{
    private readonly SearchResultSource source;
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchNoticeViewModel"/> class.
    /// </summary>
    /// <param name="source">The search results.</param>
    /// <param name="localizer">UI strings.</param>
    public SearchNoticeViewModel(SearchResultSource source, Localizer localizer)
    {
        this.source = source;
        this.localizer = localizer;
        source.SummaryChanged += (_, _) => Dispatcher.UIThread.Post(Update);
        Update();
    }

    /// <summary>
    /// Gets a value indicating whether the bar shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    /// <summary>
    /// Gets the result count line.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the semantic search could not do, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    /// <summary>
    /// Gets the text of the less relevant option, or <see langword="null"/> when there is none to show.
    /// </summary>
    [ObservableProperty]
    public partial string? LessRelevantText { get; private set; }

    /// <summary>
    /// Shows or hides the less relevant results.
    /// </summary>
    [RelayCommand]
    public void ToggleLessRelevant() => source.SetShowLessRelevant(!source.ShowLessRelevant);

    private void Update()
    {
        if (source.Summary is not { } summary)
        {
            IsVisible = false;
            return;
        }

        var similar = summary.SimilarTo is not null;
        Message = (summary.TotalCount, similar) switch
        {
            (0, false) => localizer.Get("Search_NoResults"),
            (1, false) => localizer.Get("Search_OneResult"),
            (_, false) => localizer.Format("Search_Results", summary.TotalCount),
            (0, true) => localizer.Get("Search_NoSimilarResults"),
            (1, true) => localizer.Get("Search_OneSimilarResult"),
            (_, true) => localizer.Format("Search_SimilarResults", summary.TotalCount),
        };
        Hint = summary.Semantic switch
        {
            SemanticSearchStatus.ModelMissing => localizer.Get("Search_Hint_ModelMissing"),
            SemanticSearchStatus.IndexNotReady => localizer.Get("Search_Hint_IndexNotReady"),
            SemanticSearchStatus.Failed => localizer.Get("Search_Hint_Failed"),
            SemanticSearchStatus.NotAnalyzed => localizer.Get("Search_Hint_NotAnalyzed"),
            _ => null,
        };
        LessRelevantText = summary.ShowLessRelevant
            ? localizer.Get("Search_HideLessRelevant")
            : summary.LessRelevantCount > 0 ? localizer.Format("Search_ShowLessRelevant", summary.LessRelevantCount) : null;
        IsVisible = true;
    }
}
