using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Testing.Activity;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Activity;
using MediaFileClassifier.Ui.ViewModels.Activity;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Activity.GetActivityPage;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Activity;

/// <summary>
/// The activity log window's view model (E15-S04-T02), against an in-memory log.
/// </summary>
public sealed class ActivityLogViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryActivityRepository log = new();
    private readonly ActivityChanges changes = new();
    private readonly HashSet<Id<IndexedFolder>> existing = [];
    private readonly RecordingNavigator navigator = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refresh_GroupsEventsByDay_WithTodayAndYesterdayHeaders()
    {
        Add(ActivityEventType.Import, Now.AddHours(-1));
        Add(ActivityEventType.Rescan, Now.AddHours(-2));
        Add(ActivityEventType.Analysis, Now.AddDays(-1));
        Add(ActivityEventType.Export, Now.AddDays(-5));
        var model = CreateModel();

        await model.RefreshAsync(Token);

        Assert.Equal(
            ["Today", "Import finished", "Rescan finished", "Yesterday", "Analysis finished", Now.AddDays(-5).ToString("D", System.Globalization.CultureInfo.CurrentCulture), "Export finished"],
            model.Rows.Select(row => row.IsHeader ? row.Header : row.Title));
    }

    [Fact]
    public async Task Refresh_ShowsTwoLines_WithSubjectAndNamedCounts()
    {
        Add(ActivityEventType.Rescan, Now, subject: @"C:\Photos", counts: new() { [ActivityCounts.Added] = 3, [ActivityCounts.Missing] = 1, [ActivityCounts.Failed] = 0 });
        var model = CreateModel();

        await model.RefreshAsync(Token);

        var row = Assert.Single(model.Rows, candidate => candidate.IsEvent);
        Assert.Equal((@"New: 3 · Missing: 1 · C:\Photos", Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture)), (row.Detail, row.Time));
    }

    [Fact]
    public async Task Refresh_ToleratesUnknownAndMissingCounts()
    {
        Add(ActivityEventType.Import, Now, counts: new() { ["future-count"] = 9 });
        Add(ActivityEventType.Rescan, Now.AddMinutes(-1));
        var model = CreateModel();

        await model.RefreshAsync(Token);

        Assert.Equal(["", "No changes"], model.Rows.Where(row => row.IsEvent).Select(row => row.Detail));
    }

    [Fact]
    public async Task Refresh_WithoutEvents_ShowsTheEmptyState()
    {
        var model = CreateModel();

        await model.RefreshAsync(Token);

        Assert.Equal((true, false, false), (model.IsEmpty, model.IsFilteredEmpty, model.HasMore));
    }

    [Fact]
    public async Task SelectChip_ListsOnlyItsEvents_AndErrorsHideInfo()
    {
        Add(ActivityEventType.Import, Now);
        Add(ActivityEventType.Rescan, Now.AddMinutes(-1), ActivitySeverity.Warning);
        var model = CreateModel();
        await model.RefreshAsync(Token);

        await model.SelectChipAsync(model.Chips.Single(chip => chip.Key == "Errors"));

        Assert.Equal(["Rescan finished"], model.Rows.Where(row => row.IsEvent).Select(row => row.Title));
        Assert.True(model.Chips.Single(chip => chip.Key == "Errors").IsSelected);
        Assert.False(model.Chips[0].IsSelected);
    }

    [Fact]
    public async Task SelectChip_WithNoMatch_ShowsTheFilteredEmptyState()
    {
        Add(ActivityEventType.Import, Now);
        var model = CreateModel();
        await model.RefreshAsync(Token);

        await model.SelectChipAsync(model.Chips.Single(chip => chip.Key == "Exports"));

        Assert.Equal((false, true, 0), (model.IsEmpty, model.IsFilteredEmpty, model.Rows.Count));
    }

    [Fact]
    public async Task SelectChip_OnTheChipInForce_KeepsItSelected()
    {
        var model = CreateModel();
        await model.RefreshAsync(Token);

        await model.SelectChipAsync(model.Chips[0]);

        Assert.True(model.Chips[0].IsSelected);
    }

    [Fact]
    public async Task Attach_ResetsTheFilterToAll()
    {
        Add(ActivityEventType.Import, Now);
        var model = CreateModel();
        await model.RefreshAsync(Token);
        await model.SelectChipAsync(model.Chips.Single(chip => chip.Key == "Exports"));

        model.Attach();
        await model.RefreshAsync(Token);

        Assert.Equal((true, 2), (model.Chips[0].IsSelected, model.Rows.Count));
    }

    [Fact]
    public async Task LoadMore_AddsTheNextPage_UntilTheEnd()
    {
        for (var i = 0; i < ActivityLogViewModel.PageSize + 20; i++)
        {
            Add(ActivityEventType.Import, Now.AddSeconds(-i));
        }

        var model = CreateModel();

        await model.RefreshAsync(Token);
        var first = (model.Rows.Count(row => row.IsEvent), model.HasMore);
        await model.LoadMoreAsync(Token);

        Assert.Equal(((ActivityLogViewModel.PageSize, true), (ActivityLogViewModel.PageSize + 20, false)), (first, (model.Rows.Count(row => row.IsEvent), model.HasMore)));
    }

    [Fact]
    public async Task ANewEvent_AfterAttach_IsInsertedAtTheTop_WithoutReplacingTheOtherRows()
    {
        Add(ActivityEventType.Import, Now.AddMinutes(-5));
        var posted = new List<Action>();
        var model = CreateModel(posted.Add);
        model.Attach();
        await model.RefreshAsync(Token);
        var before = model.Rows.ToList();

        Add(ActivityEventType.Rescan, Now);
        changes.Raise();
        Assert.Single(posted);
        posted[0]();
        await WaitForAsync(() => model.Rows.Count == before.Count + 1);

        Assert.Equal(("Rescan finished", true), (model.Rows[1].Title, before.All(model.Rows.Contains)));
    }

    [Fact]
    public void Detach_StopsFollowingNewEvents()
    {
        var posted = new List<Action>();
        var model = CreateModel(posted.Add);
        model.Attach();
        model.Detach();

        changes.Raise();

        Assert.Empty(posted);
    }

    [Fact]
    public async Task ShowItems_IsOnlyOnRowsWhoseFolderStillExists_AndOpensTheFolder()
    {
        var folder = Id<IndexedFolder>.New();
        existing.Add(folder);
        var gone = Id<IndexedFolder>.New();
        Add(ActivityEventType.Import, Now, folder: folder);
        Add(ActivityEventType.Import, Now.AddMinutes(-1), folder: gone);
        Add(ActivityEventType.BulkEdit, Now.AddMinutes(-2));
        var model = CreateModel();

        await model.RefreshAsync(Token);
        var rows = model.Rows.Where(row => row.IsEvent).ToList();
        rows[0].ShowItemsCommand!.Execute(null);

        Assert.Equal([true, false, false], rows.Select(row => row.CanShowItems));
        Assert.Equal([folder], navigator.Shown);
    }

    [Fact]
    public async Task LoadMore_DuringARefresh_NeitherDropsTheRefresh_NorRepeatsARow()
    {
        for (var i = 0; i < ActivityLogViewModel.PageSize + 20; i++)
        {
            Add(ActivityEventType.Import, Now.AddSeconds(-i - 10));
        }

        var sender = new LogSender(log, existing);
        var model = new ActivityLogViewModel(sender, changes, TestLocalizer.EnUs(), new FixedClock(Now), navigator, null, action => action());
        await model.RefreshAsync(Token);
        var gate = new TaskCompletionSource();
        sender.Gate = gate;
        Add(ActivityEventType.Rescan, Now);

        var refresh = model.RefreshAsync(Token);
        var more = model.LoadMoreAsync(Token);
        gate.SetResult();
        await Task.WhenAll(refresh, more);

        var events = model.Rows.Where(row => row.IsEvent).ToList();
        Assert.Equal((ActivityLogViewModel.PageSize + 21, "Rescan finished", false), (events.Count, events[0].Title, model.HasMore));
        Assert.Equal(events.Count, events.Select(row => row.Key).Distinct().Count());
    }

    [Fact]
    public async Task ALibraryChange_EmptiesTheListAtOnce_AndListsTheNewLibrary()
    {
        Add(ActivityEventType.Import, Now);
        var session = new SwitchableSession();
        var posted = new List<Action>();
        var model = new ActivityLogViewModel(new LogSender(log, existing), changes, TestLocalizer.EnUs(), new FixedClock(Now), navigator, null, posted.Add, session);
        model.Attach();
        await model.RefreshAsync(Token);
        log.Events.Clear();

        session.Raise();
        Assert.Single(posted);
        posted[0]();
        await WaitForAsync(() => model.IsEmpty);

        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task Rows_HaveAccessibleNames_ThatReadTimeTitleAndDetail()
    {
        Add(ActivityEventType.FolderAdded, Now, subject: @"D:\Media");
        var model = CreateModel();

        await model.RefreshAsync(Token);

        Assert.Contains("Folder added. D:\\Media", model.Rows.Single(row => row.IsEvent).AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DayHeaders_FollowTheDate_AfterMidnightWithTheWindowOpen()
    {
        // MFC-1034: 23:00 local time, one event an hour ago, and no new events after midnight.
        var evening = new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Unspecified);
        var clock = new ManualClock(new DateTimeOffset(evening, TimeZoneInfo.Local.GetUtcOffset(evening)).ToUniversalTime());
        Add(ActivityEventType.Import, clock.GetUtcNow().AddHours(-1));
        var model = new ActivityLogViewModel(new LogSender(log, existing), changes, TestLocalizer.EnUs(), clock, navigator, null, action => action());
        model.Attach();
        await model.RefreshAsync(Token);
        Assert.Equal("Today", model.Rows[0].Header);

        clock.Advance(TimeSpan.FromHours(2));

        await WaitForAsync(() => model.Rows[0].Header == "Yesterday");
        Assert.Equal(["Yesterday", "Import finished"], model.Rows.Select(row => row.IsHeader ? row.Header : row.Title));
        model.Detach();
    }

    [Fact]
    public async Task DayHeaders_AreNotRefreshedAfterTheWindowCloses()
    {
        var evening = new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Unspecified);
        var clock = new ManualClock(new DateTimeOffset(evening, TimeZoneInfo.Local.GetUtcOffset(evening)).ToUniversalTime());
        Add(ActivityEventType.Import, clock.GetUtcNow().AddHours(-1));
        var model = new ActivityLogViewModel(new LogSender(log, existing), changes, TestLocalizer.EnUs(), clock, navigator, null, action => action());
        model.Attach();
        await model.RefreshAsync(Token);
        model.Detach();

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal("Today", model.Rows[0].Header);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Token);
        }

        Assert.True(condition());
    }

    private ActivityLogViewModel CreateModel(Action<Action>? post = null) =>
        new(new LogSender(log, existing), changes, TestLocalizer.EnUs(), new FixedClock(Now), navigator, null, post ?? (action => action()));

    private void Add(
        ActivityEventType type,
        DateTimeOffset at,
        ActivitySeverity severity = ActivitySeverity.Info,
        string? subject = null,
        Dictionary<string, long>? counts = null,
        Id<IndexedFolder>? folder = null) =>
        log.Events.Add(new ActivityEvent(Id<ActivityEvent>.New(), type, severity, at, subject, counts, folder, null));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingNavigator : IActivityNavigator
    {
        public List<Id<IndexedFolder>> Shown { get; } = [];

        public void ShowFolder(Id<IndexedFolder> folderId) => Shown.Add(folderId);
    }

    private sealed class SwitchableSession : ILibrarySession
    {
        public event EventHandler? CurrentChanged;

        public IOpenLibrary? Current => null;

        public Task SetCurrentAsync(IOpenLibrary? library) => Task.CompletedTask;

        public void Raise() => CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LogSender(InMemoryActivityRepository log, HashSet<Id<IndexedFolder>> existing) : FakeSender
    {
        public TaskCompletionSource? Gate { get; set; }

        public override async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            return await base.Send(request, cancellationToken);
        }

        protected override object? Handle(object request)
        {
            var query = (GetActivityPageQuery)request;
            var page = log.GetPageAsync(query.Filter, query.Skip, query.Take).Result;
            var total = log.CountAsync(query.Filter).Result;
            return new ActivityPage([.. page.Select(activity => new ActivityEntry(activity, activity.FolderId is { } id && existing.Contains(id) ? id : null))], total);
        }
    }
}
