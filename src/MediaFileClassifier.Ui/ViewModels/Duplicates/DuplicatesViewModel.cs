using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediaFileClassifier.UseCases.Duplicates.Review;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// The Duplicates view (E12-S03): the open groups in a list on the left, sortable and filtered by type, and the
/// comparison of the selected group on the right. It fills the main content area in place of the media browser.
/// </summary>
public sealed partial class DuplicatesViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly ILibrarySession session;
    private readonly Localizer localizer;
    private IDuplicateGroupRepository? watched;
    private int generation;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatesViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the queries and commands.</param>
    /// <param name="session">Holds the open library.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="comparison">The comparison of the selected group.</param>
    public DuplicatesViewModel(ISender sender, ILibrarySession session, Localizer localizer, DuplicateComparisonViewModel comparison)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(comparison);

        Comparison = comparison;
        this.sender = sender;
        this.session = session;
        this.localizer = localizer;
        SortOptions =
        [
            new(DuplicateGroupSort.WastedSpace, localizer.Get("Duplicates_Sort_Wasted")),
            new(DuplicateGroupSort.Type, localizer.Get("Duplicates_Sort_Type")),
            new(DuplicateGroupSort.GroupSize, localizer.Get("Duplicates_Sort_Size")),
        ];
        SelectedSort = SortOptions[0];
        TypeFilters =
        [
            new(DuplicateTypeFilter.All, localizer.Get("Duplicates_Filter_All")) { IsSelected = true },
            new(DuplicateTypeFilter.Exact, localizer.Get("Duplicates_Filter_Exact")),
            new(DuplicateTypeFilter.Similar, localizer.Get("Duplicates_Filter_Similar")),
        ];
        Header = localizer.Get("Duplicates_Title");
        Status = string.Empty;
    }

    /// <summary>
    /// Gets the orders to choose from.
    /// </summary>
    public IReadOnlyList<DuplicateSortOption> SortOptions { get; }

    /// <summary>
    /// Gets the type chips: All, Exact and Similar.
    /// </summary>
    public IReadOnlyList<DuplicateTypeChip> TypeFilters { get; }

    /// <summary>
    /// Gets the comparison of the selected group.
    /// </summary>
    public DuplicateComparisonViewModel Comparison { get; }

    /// <summary>
    /// Gets the groups shown, in the chosen order.
    /// </summary>
    public ObservableCollection<DuplicateGroupRowViewModel> Groups { get; } = [];

    /// <summary>
    /// Gets or sets the order of the list.
    /// </summary>
    [ObservableProperty]
    public partial DuplicateSortOption SelectedSort { get; set; }

    /// <summary>
    /// Gets or sets the selected group.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DuplicateGroupRowViewModel? SelectedGroup { get; set; }

    /// <summary>
    /// Gets the header: the number of open groups and the space they waste.
    /// </summary>
    [ObservableProperty]
    public partial string Header { get; private set; }

    /// <summary>
    /// Gets the result of the last "Find duplicates" run, or an empty text.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the list is loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there are no open groups at all.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    public partial bool IsEmpty { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a group is selected.
    /// </summary>
    public bool HasSelection => SelectedGroup is not null;

    /// <summary>
    /// Gets a value indicating whether the library has open groups.
    /// </summary>
    public bool HasGroups => !IsEmpty;

    /// <summary>
    /// Loads the list and starts following the library's groups. Call it when the view is shown.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    public Task ActivateAsync()
    {
        Watch();
        Comparison.ReloadRules();
        return RefreshAsync();
    }

    /// <summary>
    /// Stops following the library's groups. Call it when the view is replaced.
    /// </summary>
    public void Deactivate()
    {
        if (watched is not null)
        {
            watched.Changed -= OnGroupsChanged;
            watched = null;
        }
    }

    /// <summary>
    /// Selects a type chip and shows only that type.
    /// </summary>
    /// <param name="chip">The chip.</param>
    /// <returns>A task that completes when the list is updated.</returns>
    [RelayCommand]
    public async Task SelectTypeAsync(DuplicateTypeChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        foreach (var other in TypeFilters)
        {
            other.IsSelected = ReferenceEquals(other, chip);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Runs the full detection pass as a background job (E12-S01-T03).
    /// </summary>
    /// <returns>A task that completes when the job is queued.</returns>
    [RelayCommand]
    public async Task FindDuplicatesAsync()
    {
        var result = await sender.Send(new DetectDuplicatesCommand()).ConfigureAwait(true);
        Status = localizer.Get(result.Outcome switch
        {
            DetectDuplicatesOutcome.Started => "Duplicates_Find_Started",
            DetectDuplicatesOutcome.Busy => "Duplicates_Find_Busy",
            DetectDuplicatesOutcome.NothingToDo => "Duplicates_Find_NothingToDo",
            _ => "Duplicates_Find_Unavailable",
        });
    }

    /// <summary>
    /// Loads the groups again, keeping the selection when its group is still listed.
    /// </summary>
    /// <returns>A task that completes when the list is updated.</returns>
    public async Task RefreshAsync()
    {
        var mine = ++generation;
        IsLoading = Groups.Count == 0;
        var filter = TypeFilters.First(chip => chip.IsSelected).Value;
        var list = await sender.Send(new GetDuplicateGroupsQuery(SelectedSort.Value, filter)).ConfigureAwait(true);
        if (mine != generation)
        {
            return;
        }

        var selected = SelectedGroup?.Summary.Id;
        Groups.Clear();
        foreach (var summary in list.Groups)
        {
            Groups.Add(new DuplicateGroupRowViewModel(summary, localizer));
        }

        SelectedGroup = Groups.FirstOrDefault(row => row.Summary.Id == selected) ?? Groups.FirstOrDefault();
        IsEmpty = list.TotalGroups == 0;
        Header = list.TotalGroups == 0
            ? localizer.Get("Duplicates_Title")
            : localizer.Format("Duplicates_Header", list.TotalGroups, Format.FileSize(list.TotalWastedBytes));
        IsLoading = false;
    }

    /// <summary>
    /// Selects the next group in the list (Down).
    /// </summary>
    [RelayCommand]
    public void SelectNext() => Step(1);

    /// <summary>
    /// Selects the previous group in the list (Up).
    /// </summary>
    [RelayCommand]
    public void SelectPrevious() => Step(-1);

    partial void OnSelectedSortChanged(DuplicateSortOption value) => _ = RefreshAsync();

    partial void OnSelectedGroupChanged(DuplicateGroupRowViewModel? value) => _ = Comparison.ShowAsync(value?.Summary.Id);

    private void Step(int step)
    {
        if (Groups.Count == 0)
        {
            return;
        }

        var index = SelectedGroup is null ? 0 : Groups.IndexOf(SelectedGroup) + step;
        SelectedGroup = Groups[Math.Clamp(index, 0, Groups.Count - 1)];
    }

    private void Watch()
    {
        var duplicates = session.Current?.Duplicates;
        if (ReferenceEquals(duplicates, watched))
        {
            return;
        }

        Deactivate();
        watched = duplicates;
        if (watched is not null)
        {
            watched.Changed += OnGroupsChanged;
        }
    }

    // Reconciling raises Changed once per batch on a pool thread; the list reloads on the UI thread.
    private void OnGroupsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
}

/// <summary>
/// An order the list can be sorted in.
/// </summary>
/// <param name="Value">The order.</param>
/// <param name="Label">Its name.</param>
public sealed record DuplicateSortOption(DuplicateGroupSort Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>
/// A chip that filters the list by group type.
/// </summary>
public sealed partial class DuplicateTypeChip : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateTypeChip"/> class.
    /// </summary>
    /// <param name="value">The filter.</param>
    /// <param name="label">The chip's text.</param>
    public DuplicateTypeChip(DuplicateTypeFilter value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>
    /// Gets the filter.
    /// </summary>
    public DuplicateTypeFilter Value { get; }

    /// <summary>
    /// Gets the chip's text.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the chip is selected.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
