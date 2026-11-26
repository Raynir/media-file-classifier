using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Activity;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Activity.GetActivityPage;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Activity;

/// <summary>
/// The activity log window (E15-S04-T02): what the app did to the library, newest first and grouped by day, with
/// filter chips, paging and a live refresh. The window only reads the log.
/// </summary>
public sealed partial class ActivityLogViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// How many events one page loads.
    /// </summary>
    public const int PageSize = 100;

    /// <summary>
    /// How often the window checks whether the day changed, so "Today" becomes "Yesterday" (MFC-1034).
    /// </summary>
    public static readonly TimeSpan DayCheckInterval = TimeSpan.FromMinutes(1);

    private readonly ISender sender;
    private readonly ActivityChanges changes;
    private readonly Localizer localizer;
    private readonly TimeProvider clock;
    private readonly ActivityPresentation presentation;
    private readonly IActivityNavigator? navigator;
    private readonly Action<Action> post;
    private readonly ILogger logger;
    private readonly List<ActivityEntry> entries = [];
    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly ILibrarySession? session;
    private bool attached;
    private ITimer? dayCheck;
    private DateOnly shownDay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogViewModel"/> class.
    /// </summary>
    /// <param name="sender">Gets the pages.</param>
    /// <param name="changes">Raised when an event was recorded.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="clock">Clock, for the day headers.</param>
    /// <param name="navigator">Runs "Show items", or <see langword="null"/> for no links.</param>
    /// <param name="logger">Logger, or <see langword="null"/> for none.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    /// <param name="session">Holds the open library, so the list is emptied when it closes or another opens, or <see langword="null"/> to ignore that.</param>
    public ActivityLogViewModel(
        ISender sender,
        ActivityChanges changes,
        Localizer localizer,
        TimeProvider clock,
        IActivityNavigator? navigator = null,
        ILogger<ActivityLogViewModel>? logger = null,
        Action<Action>? post = null,
        ILibrarySession? session = null)
    {
        this.session = session;
        this.sender = sender;
        this.changes = changes;
        this.localizer = localizer;
        this.clock = clock;
        this.navigator = navigator;
        this.logger = logger ?? NullLogger<ActivityLogViewModel>.Instance;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        presentation = new ActivityPresentation(localizer);
        Chips =
        [
            new ActivityChipViewModel("All", localizer.Get("Activity_Chip_All"), ActivityFilter.All),
            Chip("Imports", [ActivityEventType.Import]),
            Chip("Rescans", [ActivityEventType.Rescan]),
            Chip("Analysis", [ActivityEventType.Analysis]),
            Chip("Edits", [ActivityEventType.BulkEdit]),
            Chip("Folders", [ActivityEventType.FolderAdded, ActivityEventType.FolderRemoved, ActivityEventType.FolderMonitoring, ActivityEventType.FolderRelinked, ActivityEventType.FolderDriveReplaced]),
            Chip("Duplicates", [ActivityEventType.DuplicateResolution]),
            Chip("Exports", [ActivityEventType.Export, ActivityEventType.LibraryImport]),
            new ActivityChipViewModel("Errors", localizer.Get("Activity_Chip_Errors"), new ActivityFilter(null, ActivitySeverity.Warning)),
        ];
        Chips[0].IsSelected = true;
        SelectedChip = Chips[0];
    }

    /// <summary>
    /// Raised when the window asks to be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the filter chips, in order.
    /// </summary>
    public IReadOnlyList<ActivityChipViewModel> Chips { get; }

    /// <summary>
    /// Gets the list: day headers and events, newest first.
    /// </summary>
    public ObservableCollection<ActivityRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets the chip in force.
    /// </summary>
    public ActivityChipViewModel SelectedChip { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the log has no events at all.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a filter hides every event.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFilteredEmpty { get; private set; }

    /// <summary>
    /// Gets a value indicating whether older events can be loaded.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    /// <summary>
    /// Gets how many events are listed of how many match, e.g. "Showing 100 of 240".
    /// </summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        attached = false;
        dayCheck?.Dispose();
        dayCheck = null;
        loading.Dispose();
    }

    /// <summary>
    /// Starts following new events; call it when the window opens.
    /// </summary>
    public void Attach()
    {
        if (!attached)
        {
            attached = true;
            changes.Changed += OnChanged;
            // The day headers are relative to today (MFC-1034). A minute's check, not a timer set for midnight: such a
            // timer doesn't count the time the PC sleeps, and the clock or the time zone may change.
            dayCheck ??= clock.CreateTimer(_ => post(OnDayCheck), null, DayCheckInterval, DayCheckInterval);
            if (session is not null)
            {
                session.CurrentChanged += OnLibraryChanged;
            }

            // Every opening starts on All, so a filter left on earlier never hides new events unnoticed.
            if (SelectedChip != Chips[0])
            {
                SelectedChip.IsSelected = false;
                Chips[0].IsSelected = true;
                SelectedChip = Chips[0];
                OnPropertyChanged(nameof(SelectedChip));
                entries.Clear();
                Rows.Clear();
            }
        }
    }

    /// <summary>
    /// Stops following new events; call it when the window closes.
    /// </summary>
    public void Detach()
    {
        attached = false;
        dayCheck?.Dispose();
        dayCheck = null;
        changes.Changed -= OnChanged;
        if (session is not null)
        {
            session.CurrentChanged -= OnLibraryChanged;
        }
    }

    /// <summary>
    /// Lists the newest events again, keeping the ones already loaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the list is up to date.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // One load at a time, each reading what the list holds when its turn comes, so a refresh is never dropped
        // by a page load that started later, and a page never starts from a stale count.
        await loading.WaitAsync(cancellationToken);
        try
        {
            var chip = SelectedChip;
            ActivityPage page;
            try
            {
                page = await sender.Send(new GetActivityPageQuery(chip.Filter, 0, Math.Max(PageSize, entries.Count)), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The library closed under us or the database was busy; the next change tries again.
                LogRefreshFailed(exception);
                return;
            }

            // The chip changed while the page was read: its own refresh follows.
            if (chip != SelectedChip)
            {
                return;
            }

            entries.Clear();
            entries.AddRange(page.Entries);
            Apply(page.Total);
        }
        finally
        {
            loading.Release();
        }
    }

    /// <summary>
    /// Loads the next page of older events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the page is listed.</returns>
    [RelayCommand]
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        await loading.WaitAsync(cancellationToken);
        try
        {
            if (!HasMore)
            {
                return;
            }

            var chip = SelectedChip;
            ActivityPage page;
            try
            {
                page = await sender.Send(new GetActivityPageQuery(chip.Filter, entries.Count, PageSize), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRefreshFailed(exception);
                return;
            }

            if (chip != SelectedChip)
            {
                return;
            }

            // An event recorded since the last page shifts the list by one, so the page may repeat the last row.
            var known = entries.Select(entry => entry.Event.Id).ToHashSet();
            entries.AddRange(page.Entries.Where(entry => !known.Contains(entry.Event.Id)));
            Apply(page.Total);
        }
        finally
        {
            loading.Release();
        }
    }

    /// <summary>
    /// Applies a filter chip and lists its events.
    /// </summary>
    /// <param name="chip">The chip.</param>
    /// <returns>A task that completes when the list shows the chip's events.</returns>
    [RelayCommand]
    public async Task SelectChipAsync(ActivityChipViewModel? chip)
    {
        if (chip is null || chip == SelectedChip)
        {
            // Clicking the chip in force keeps it on, like a radio button.
            chip?.IsSelected = true;
            return;
        }

        SelectedChip.IsSelected = false;
        chip.IsSelected = true;
        SelectedChip = chip;
        OnPropertyChanged(nameof(SelectedChip));
        entries.Clear();
        Rows.Clear();
        await RefreshAsync();
    }

    private void OnChanged(object? sender, EventArgs e) => post(() => _ = RefreshAsync());

    // The events belong to a library, so the ones listed are gone with it.
    private void OnLibraryChanged(object? sender, EventArgs e) => post(() =>
    {
        entries.Clear();
        Rows.Clear();
        _ = RefreshAsync();
    });

    private ActivityChipViewModel Chip(string key, ActivityEventType[] types) =>
        new(key, localizer.Get($"Activity_Chip_{key}"), new ActivityFilter(types));

    private void Apply(int total)
    {
        var rows = new List<ActivityRowViewModel>(entries.Count + 8);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        shownDay = today;
        DateOnly? day = null;
        foreach (var entry in entries)
        {
            var local = TimeZoneInfo.ConvertTime(entry.Event.OccurredAt, clock.LocalTimeZone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date != day)
            {
                day = date;
                rows.Add(ActivityRowViewModel.ForDay(date, DayText(date, today)));
            }

            rows.Add(ActivityRowViewModel.ForEvent(
                entry,
                presentation.Title(entry.Event),
                presentation.Detail(entry.Event),
                local.ToString("t", CultureInfo.CurrentCulture),
                navigator is null ? null : ShowItems));
        }

        Sync(rows);
        var all = SelectedChip.Filter == ActivityFilter.All;
        IsEmpty = entries.Count == 0 && all;
        IsFilteredEmpty = entries.Count == 0 && !all;
        HasMore = entries.Count < total;
        CountText = entries.Count == 0 ? string.Empty : localizer.Format("Activity_Showing", entries.Count, total);
    }

    // Inserts and removes only what differs, so a refresh that adds events at the top leaves the scroll position alone.
    private void Sync(List<ActivityRowViewModel> rows)
    {
        // Rows before i already match, and keys are unique, so a key in this set is still ahead.
        var wanted = rows.Select(row => row.Key).ToHashSet();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < Rows.Count && Rows[i].Key == rows[i].Key)
            {
                // A day header's text changes at midnight: "Today" becomes "Yesterday" (MFC-1034).
                if (Rows[i].Header != rows[i].Header)
                {
                    Rows[i] = rows[i];
                }

                continue;
            }

            if (i < Rows.Count && !wanted.Contains(Rows[i].Key))
            {
                // A row that is gone, e.g. trimmed or filtered out.
                Rows.RemoveAt(i--);
                continue;
            }

            Rows.Insert(i, rows[i]);
        }

        while (Rows.Count > rows.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    // The list is refreshed when the day has changed since its headers were made (MFC-1034).
    private void OnDayCheck()
    {
        if (attached && Rows.Count > 0 && DateOnly.FromDateTime(clock.GetLocalNow().DateTime) != shownDay)
        {
            _ = RefreshAsync();
        }
    }

    private string DayText(DateOnly date, DateOnly today) =>
        date == today ? localizer.Get("Activity_Today")
        : date == today.AddDays(-1) ? localizer.Get("Activity_Yesterday")
        : date.ToString("D", CultureInfo.CurrentCulture);

    private void ShowItems(ActivityRowViewModel row)
    {
        if (row.Entry?.ShowFolderId is { } folderId)
        {
            navigator?.ShowFolder(folderId);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void LogRefreshFailed(Exception exception) =>
        logger.LogWarning(exception, "The activity log could not be listed; the next change tries again");
}

/// <summary>
/// One filter chip of the activity log.
/// </summary>
public sealed partial class ActivityChipViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityChipViewModel"/> class.
    /// </summary>
    /// <param name="key">The chip's name.</param>
    /// <param name="label">The chip's text.</param>
    /// <param name="filter">The events the chip shows.</param>
    public ActivityChipViewModel(string key, string label, ActivityFilter filter)
    {
        Key = key;
        Label = label;
        Filter = filter;
    }

    /// <summary>
    /// Gets the chip's name, e.g. <c>Imports</c>.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the chip's text.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the events the chip shows.
    /// </summary>
    public ActivityFilter Filter { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the chip is the one in force.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
