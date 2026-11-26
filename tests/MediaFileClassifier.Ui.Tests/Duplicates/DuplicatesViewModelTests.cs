using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.ViewModels.Duplicates;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediaFileClassifier.UseCases.Duplicates.Review;

namespace MediaFileClassifier.Ui.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="DuplicatesViewModel"/>: the list, its sorting and filtering, and the full detection pass (E12-S03-T01).
/// </summary>
public sealed class DuplicatesViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_ListsTheGroupsWithTheirWastedSpace_AndSummarisesThemInTheHeader()
    {
        var sender = new ListSender(Group(2, 82_000_000), Group(3, 41_000_000));
        var model = Create(sender);

        await model.RefreshAsync();

        Assert.Equal(2, model.Groups.Count);
        Assert.Equal("2 files", model.Groups[0].FilesText);
        Assert.Contains("wasted", model.Groups[0].WastedText);
        Assert.Contains("2 groups", model.Header);
        Assert.False(model.IsEmpty);
        Assert.Same(model.Groups[0], model.SelectedGroup);
    }

    [Fact]
    public async Task Refresh_ByDefault_AsksForTheMostWastedSpaceFirst()
    {
        var sender = new ListSender(Group(2, 1));
        var model = Create(sender);

        await model.RefreshAsync();

        Assert.Equal(DuplicateGroupSort.WastedSpace, sender.LastQuery!.Sort);
        Assert.Equal(DuplicateTypeFilter.All, sender.LastQuery.Filter);
    }

    [Fact]
    public async Task ChoosingASortOrder_LoadsTheListInThatOrder()
    {
        var sender = new ListSender(Group(2, 1));
        var model = Create(sender);
        await model.RefreshAsync();

        model.SelectedSort = model.SortOptions.Single(option => option.Value == DuplicateGroupSort.GroupSize);
        await sender.WaitForQueryAsync(DuplicateGroupSort.GroupSize);

        Assert.Equal(DuplicateGroupSort.GroupSize, sender.LastQuery!.Sort);
    }

    [Fact]
    public async Task SelectingATypeChip_FiltersTheListAndMarksOnlyThatChip()
    {
        var sender = new ListSender(Group(2, 1));
        var model = Create(sender);
        await model.RefreshAsync();
        var exact = model.TypeFilters.Single(chip => chip.Value == DuplicateTypeFilter.Exact);

        await model.SelectTypeCommand.ExecuteAsync(exact);

        Assert.Equal(DuplicateTypeFilter.Exact, sender.LastQuery!.Filter);
        Assert.Equal([exact], model.TypeFilters.Where(chip => chip.IsSelected));
    }

    [Fact]
    public async Task Refresh_WithoutOpenGroups_ShowsTheEmptyState()
    {
        var model = Create(new ListSender());

        await model.RefreshAsync();

        Assert.True(model.IsEmpty);
        Assert.False(model.HasGroups);
        Assert.False(model.HasSelection);
        Assert.Empty(model.Groups);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionWhenItsGroupIsStillListed()
    {
        var first = Group(2, 5);
        var second = Group(2, 4);
        var sender = new ListSender(first, second);
        var model = Create(sender);
        await model.RefreshAsync();
        model.SelectedGroup = model.Groups[1];

        await model.RefreshAsync();

        Assert.Equal(second.Id, model.SelectedGroup!.Summary.Id);
    }

    [Theory]
    [InlineData(DetectDuplicatesOutcome.Started, "background")]
    [InlineData(DetectDuplicatesOutcome.Busy, "already running")]
    [InlineData(DetectDuplicatesOutcome.NothingToDo, "nothing to find")]
    [InlineData(DetectDuplicatesOutcome.Unavailable, "Open a library")]
    public async Task FindDuplicates_ReportsWhatTheFullPassDid(DetectDuplicatesOutcome outcome, string expected)
    {
        var sender = new ListSender { Detect = new DetectDuplicatesResult(outcome) };
        var model = Create(sender);

        await model.FindDuplicatesCommand.ExecuteAsync(null);

        Assert.Contains(expected, model.Status);
    }

    private static DuplicatesViewModel Create(ListSender sender) =>
        new(sender, new FakeBrowserSession(open: false), TestLocalizer.EnUs());

    private static DuplicateGroupSummary Group(int members, long wasted) =>
        new(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, members, wasted, Start);

    private sealed class ListSender(params DuplicateGroupSummary[] groups) : FakeSender
    {
        private readonly List<DuplicateGroupSort> sorts = [];

        public GetDuplicateGroupsQuery? LastQuery { get; private set; }

        public DetectDuplicatesResult Detect { get; init; } = new(DetectDuplicatesOutcome.Started);

        public async Task WaitForQueryAsync(DuplicateGroupSort sort)
        {
            while (!sorts.Contains(sort))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }

        protected override object? Handle(object request)
        {
            switch (request)
            {
                case GetDuplicateGroupsQuery query:
                    LastQuery = query;
                    sorts.Add(query.Sort);
                    return DuplicateGroupList.Create(groups, query.Sort, query.Filter);
                case DetectDuplicatesCommand:
                    return Detect;
                default:
                    return base.Handle(request);
            }
        }
    }
}
