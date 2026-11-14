using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// The filters above the results (E10-S02): the quick chips (All, Images, Videos, Favorites) and the "Filters" button
/// whose flyout holds a control for every available filter. It reads and writes
/// the filters through <see cref="SearchResultSource.SetFilters"/>, so it follows changes made elsewhere too.
/// </summary>
public sealed partial class FilterBarViewModel : ViewModelBase
{
    /// <summary>
    /// The ID of the media type filter the Images and Videos chips set.
    /// </summary>
    public const string MediaTypeFilterId = "media-type";

    /// <summary>
    /// The ID of the favorites filter (M3); its chip shows once a filter with this ID is available.
    /// </summary>
    public const string FavoritesFilterId = "favorite";

    private static readonly string[] ImageTypes = ["AnimatedImage", "Image"];
    private static readonly string[] VideoTypes = ["Video"];

    private readonly SearchResultSource source;
    private readonly FilterRegistry registry;
    private readonly Localizer localizer;
    private readonly QuickFilterViewModel all;
    private readonly QuickFilterViewModel images;
    private readonly QuickFilterViewModel videos;
    private readonly QuickFilterViewModel favorites;
    private List<Task> optionLoads = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The search results the filters apply to.</param>
    /// <param name="registry">The registered filters.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="persistence">Keeps the filters across restarts (E10-S02-T03). The bar doesn't call it; taking it
    /// makes the container build it together with the filter UI, so it listens from the start.</param>
    public FilterBarViewModel(SearchResultSource source, FilterRegistry registry, Localizer localizer, FilterStatePersistence? persistence = null)
    {
        _ = persistence;
        this.source = source;
        this.registry = registry;
        this.localizer = localizer;
        all = new QuickFilterViewModel(localizer.Get("Filter_Quick_All"), () => Apply(state => state.Remove(MediaTypeFilterId)));
        images = new QuickFilterViewModel(localizer.Get("Filter_Quick_Images"), () => Apply(state => state.Set(MediaTypeFilterId, new MultiSelectValue(ImageTypes))));
        videos = new QuickFilterViewModel(localizer.Get("Filter_Quick_Videos"), () => Apply(state => state.Set(MediaTypeFilterId, new MultiSelectValue(VideoTypes))));
        favorites = new QuickFilterViewModel(
            localizer.Get("Filter_Quick_Favorites"),
            () => Apply(state => state.Get(FavoritesFilterId) is BooleanValue { Value: true } ? state.Remove(FavoritesFilterId) : state.Set(FavoritesFilterId, new BooleanValue(true))));

        foreach (var filter in registry.All)
        {
            filter.AvailabilityChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        }

        source.Changed += (_, _) => Dispatcher.UIThread.Post(Sync);
        Rebuild();
    }

    /// <summary>
    /// Gets a control for each available filter, in panel order.
    /// </summary>
    public ObservableCollection<FilterEditorViewModel> Editors { get; } = [];

    /// <summary>
    /// Gets the quick filter chips that apply now.
    /// </summary>
    public ObservableCollection<QuickFilterViewModel> QuickFilters { get; } = [];

    /// <summary>
    /// Gets a chip for each filter in force, in panel order (E10-S02-T02).
    /// </summary>
    public ObservableCollection<FilterChipViewModel> Chips { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the bar shows: there is a filter to set.
    /// </summary>
    public bool IsVisible => Editors.Count > 0;

    /// <summary>
    /// Gets the number of filters in force.
    /// </summary>
    [ObservableProperty]
    public partial int ActiveCount { get; private set; }

    /// <summary>
    /// Gets the text of the Filters button, with the number of filters in force.
    /// </summary>
    public string ButtonText => ActiveCount == 0 ? localizer.Get("Filter_Button") : localizer.Format("Filter_ButtonActive", ActiveCount);

    /// <summary>
    /// Gets a task that completes when the options of the multi-select filters are loaded.
    /// </summary>
    public Task OptionsLoaded => Task.WhenAll(optionLoads);

    /// <summary>
    /// Removes every filter in force (E10-S02-T02).
    /// </summary>
    [RelayCommand]
    public void ClearAll() => source.SetFilters(Editors.Aggregate(source.Filters, (state, editor) => state.Remove(editor.Id)));

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(ButtonText));

    private static bool Is(FilterState state, string id, IReadOnlyList<string> types) =>
        state.Get(id) is MultiSelectValue multi && multi.Values.SequenceEqual(types, StringComparer.Ordinal);

    private void Apply(Func<FilterState, FilterState> change) => source.SetFilters(change(source.Filters));

    private void OnEditorChanged(FilterEditorViewModel editor) => source.SetFilters(source.Filters.Set(editor.Id, editor.Value));

    private void Rebuild()
    {
        foreach (var editor in Editors)
        {
            editor.ValueChanged -= OnEditorValueChanged;
        }

        Editors.Clear();
        optionLoads = [];
        foreach (var filter in registry.Available)
        {
            if (FilterEditorFactory.TryCreate(filter, localizer) is not { } editor)
            {
                continue;
            }

            editor.ValueChanged += OnEditorValueChanged;
            if (editor is MultiSelectFilterViewModel multi)
            {
                optionLoads.Add(LoadOptionsAsync(multi));
            }

            Editors.Add(editor);
        }

        OnPropertyChanged(nameof(IsVisible));
        Sync();
    }

    private async Task LoadOptionsAsync(MultiSelectFilterViewModel editor)
    {
        try
        {
            await editor.LoadOptionsAsync().ConfigureAwait(true);
            editor.Load(source.Filters.Get(editor.Id));
            UpdateChips(source.Filters);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The check boxes stay empty; the other filters still work.
        }
    }

    private void OnEditorValueChanged(object? sender, EventArgs e)
    {
        if (sender is FilterEditorViewModel editor)
        {
            OnEditorChanged(editor);
        }
    }

    // Shows the active state in the controls and chips. A control whose value already matches is left alone, so text
    // being typed isn't rewritten.
    private void Sync()
    {
        var state = source.Filters;
        foreach (var editor in Editors)
        {
            var value = state.Get(editor.Id);
            if (!Equals(editor.Value, value))
            {
                editor.Load(value);
            }
        }

        ActiveCount = Editors.Count(editor => state.Get(editor.Id) is not null);
        UpdateQuickFilters(state);
        UpdateChips(state);
    }

    private void UpdateChips(FilterState state)
    {
        var chips = new List<FilterChipViewModel>();
        foreach (var editor in Editors)
        {
            if (state.Get(editor.Id) is { } value)
            {
                var text = editor.Describe(value);
                chips.Add(new FilterChipViewModel(
                    editor.Id,
                    text.Length == 0 ? editor.Title : localizer.Format("Filter_Chip", editor.Title, text),
                    localizer.Format("Filter_ChipRemove", editor.Title),
                    () => Apply(current => current.Remove(editor.Id))));
            }
        }

        if (!chips.SequenceEqual(Chips))
        {
            Chips.Clear();
            foreach (var chip in chips)
            {
                Chips.Add(chip);
            }
        }
    }

    private void UpdateQuickFilters(FilterState state)
    {
        all.IsChecked = state.Get(MediaTypeFilterId) is null;
        images.IsChecked = Is(state, MediaTypeFilterId, ImageTypes);
        videos.IsChecked = Is(state, MediaTypeFilterId, VideoTypes);
        favorites.IsChecked = state.Get(FavoritesFilterId) is BooleanValue { Value: true };

        var wanted = new List<QuickFilterViewModel>();
        if (Editors.Any(editor => editor.Id == MediaTypeFilterId))
        {
            wanted.AddRange([all, images, videos]);
        }

        if (Editors.Any(editor => editor.Id == FavoritesFilterId && editor is BooleanFilterViewModel))
        {
            wanted.Add(favorites);
        }

        if (!wanted.SequenceEqual(QuickFilters))
        {
            QuickFilters.Clear();
            foreach (var chip in wanted)
            {
                QuickFilters.Add(chip);
            }
        }
    }
}
