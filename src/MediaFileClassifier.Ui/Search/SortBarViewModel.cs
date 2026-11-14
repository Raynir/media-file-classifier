using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The sort control at the end of the results header (E10-S04-T02): a dropdown of sort modes and a button for the
/// direction. Relevance is offered only while searching, and has no direction. It reads and writes the order through
/// <see cref="SearchResultSource.SetSort"/>, which remembers it separately for searching and browsing, so it follows
/// the context as the search text comes and goes.
/// </summary>
public sealed partial class SortBarViewModel : ViewModelBase
{
    private readonly SearchResultSource source;
    private readonly Localizer localizer;
    private readonly SortOptionViewModel[] options;
    private bool isSyncing;

    /// <summary>
    /// Initializes a new instance of the <see cref="SortBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to sort.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="persistence">Keeps the orders across restarts. The bar doesn't call it; taking it makes the
    /// container build it with the sort UI, so the remembered orders apply from the start.</param>
    public SortBarViewModel(SearchResultSource source, Localizer localizer, SortStatePersistence? persistence = null)
    {
        _ = persistence;
        this.source = source;
        this.localizer = localizer;
        options =
        [
            new(SortMode.Relevance, localizer.Get("Sort_Relevance")),
            new(SortMode.Name, localizer.Get("Sort_Name")),
            new(SortMode.Date, localizer.Get("Sort_Date")),
            new(SortMode.Size, localizer.Get("Sort_Size")),
            new(SortMode.ImportDate, localizer.Get("Sort_ImportDate")),
        ];
        source.Changed += (_, _) => Dispatcher.UIThread.Post(Sync);
        Sync();
    }

    /// <summary>
    /// Gets the modes offered now: every mode while searching, all but relevance while browsing.
    /// </summary>
    public ObservableCollection<SortOptionViewModel> Modes { get; } = [];

    /// <summary>
    /// Gets or sets the mode chosen. Picking another one sorts by it in its usual direction: names A to Z, the others
    /// newest or largest first.
    /// </summary>
    [ObservableProperty]
    public partial SortOptionViewModel? SelectedMode { get; set; }

    /// <summary>
    /// Gets a value indicating whether the order runs descending.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDescending { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the direction button shows: not for relevance, which is always best first.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirectionVisible { get; private set; }

    /// <summary>
    /// Gets the direction's name, for the button's accessible name and tooltip: "Ascending" or "Descending".
    /// </summary>
    [ObservableProperty]
    public partial string DirectionText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the arrow on the direction button.
    /// </summary>
    [ObservableProperty]
    public partial string DirectionGlyph { get; private set; } = string.Empty;

    [RelayCommand]
    private void ToggleDirection()
    {
        var sort = source.Sort;
        if (sort.Mode != SortMode.Relevance)
        {
            source.SetSort(sort with { Descending = !sort.Descending });
        }
    }

    partial void OnSelectedModeChanged(SortOptionViewModel? value)
    {
        if (!isSyncing && value is not null && value.Mode != source.Sort.Mode)
        {
            source.SetSort(SortChoice.Natural(value.Mode));
        }
    }

    private void Sync()
    {
        isSyncing = true;
        try
        {
            var offered = options.Where(option => source.IsSearchContext || option.Mode != SortMode.Relevance).ToList();
            if (!Modes.SequenceEqual(offered))
            {
                Modes.Clear();
                foreach (var option in offered)
                {
                    Modes.Add(option);
                }
            }

            var sort = source.Sort;
            SelectedMode = offered.FirstOrDefault(option => option.Mode == sort.Mode);
            IsDescending = sort.Descending;
            IsDirectionVisible = sort.Mode != SortMode.Relevance;
            DirectionText = localizer.Get(sort.Descending ? "Sort_Descending" : "Sort_Ascending");
            DirectionGlyph = sort.Descending ? "↓" : "↑";
        }
        finally
        {
            isSyncing = false;
        }
    }
}
