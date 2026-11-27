using System.Text.Json;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Duplicates;
using MediaFileClassifier.UseCases.Duplicates.Review;

namespace MediaFileClassifier.Ui.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="DuplicateComparisonViewModel"/>: the cards and their compared attributes, the keeper choice
/// and its suggestion, the keeper rule order and the zoom compare toggle (E12-S03-T02 to T04).
/// </summary>
public sealed class DuplicateComparisonViewModelTests
{
    // MFC-142 adds the Visual type; until then any value other than Exact stands in for it.
    private const DuplicateGroupType NotExact = (DuplicateGroupType)1;

    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Show_FillsOneCardPerMember_AndTintsOnlyTheRowsThatDiffer()
    {
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(size: 8_100_000, format: "jpg"), Member(size: 8_100_000, format: "jpg"), Member(size: 1_200_000, format: "webp")));
        var model = DuplicateTestBed.Comparison(sender);

        await model.ShowAsync(sender.Detail.GroupId);

        Assert.Equal(3, model.Members.Count);
        var cells = model.Members[0].Cells.ToDictionary(cell => cell.Label);
        Assert.False(cells["Resolution"].Differs);
        Assert.True(cells["File size"].Differs);
        Assert.True(cells["Format"].Differs);
        Assert.False(cells["Date taken"].Differs);
        Assert.Equal("JPG", cells["Format"].Text);
        Assert.Equal("WEBP", model.Members[2].Cells.Single(cell => cell.Label == "Format").Text);
        Assert.Equal("4032×3024", cells["Resolution"].Text);
    }

    [Fact]
    public async Task Show_ShowsTheUsersMetadataOfEachMember()
    {
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(tags: 3, favorite: true), Member()));
        var model = DuplicateTestBed.Comparison(sender);

        await model.ShowAsync(sender.Detail.GroupId);

        Assert.Equal("3 tags, Favorite", model.Members[0].Cells.Single(cell => cell.Label == "Your metadata").Text);
        Assert.Equal("None", model.Members[1].Cells.Single(cell => cell.Label == "Your metadata").Text);
        Assert.True(model.Members[0].Cells.Single(cell => cell.Label == "Your metadata").Differs);
    }

    [Fact]
    public async Task Show_SelectsTheSuggestedKeeper_AndNamesTheRule()
    {
        var members = new[] { Member(), Member() };
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, members) with { Suggestion = new KeeperSuggestion(members[1].ItemId, KeeperRule.LargestFile) });
        var model = DuplicateTestBed.Comparison(sender);

        await model.ShowAsync(sender.Detail.GroupId);

        Assert.Same(model.Members[1], model.Keeper);
        Assert.True(model.Members[1].IsKeeper);
        Assert.True(model.Members[1].IsSuggested);
        Assert.False(model.Members[0].IsKeeper);
        Assert.Contains("Largest file", model.SuggestionText);
    }

    [Fact]
    public async Task SetKeeper_MovesTheChoice_ButNotToAFileThatCantBeKept()
    {
        var members = new[] { Member(), Member(), Member(available: false) };
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, members) with { Suggestion = new KeeperSuggestion(members[0].ItemId, null) });
        var model = DuplicateTestBed.Comparison(sender);
        await model.ShowAsync(sender.Detail.GroupId);

        model.SetKeeperCommand.Execute(model.Members[1]);
        Assert.Same(model.Members[1], model.Keeper);
        Assert.False(model.Members[0].IsKeeper);

        model.SetKeeperCommand.Execute(model.Members[2]);
        Assert.Same(model.Members[1], model.Keeper);
        Assert.True(model.Members[2].IsUnavailable);
    }

    [Fact]
    public async Task Show_WithNoFileThatCanBeKept_HasNoKeeper()
    {
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(available: false), Member(available: false)));
        var model = DuplicateTestBed.Comparison(sender);

        await model.ShowAsync(sender.Detail.GroupId);

        Assert.Null(model.Keeper);
        Assert.Contains("No file", model.SuggestionText);
    }

    [Fact]
    public async Task FocusAndSpace_SetTheKeeperFromTheKeyboard()
    {
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(), Member(), Member()));
        var model = DuplicateTestBed.Comparison(sender);
        await model.ShowAsync(sender.Detail.GroupId);

        model.MoveFocus(1);
        model.MoveFocus(1);
        model.MoveFocus(1);
        model.SetFocusedKeeperCommand.Execute(null);

        Assert.Equal(2, model.FocusedIndex);
        Assert.True(model.Members[2].IsCurrent);
        Assert.Same(model.Members[2], model.Keeper);
    }

    [Theory]
    [InlineData(DuplicateGroupType.Exact, false)]
    [InlineData(NotExact, true)]
    public async Task ToggleZoom_IsOnlyOfferedForVisualGroups(DuplicateGroupType type, bool expected)
    {
        var sender = new GroupSender(Detail(type, Member(), Member()));
        var model = DuplicateTestBed.Comparison(sender);
        await model.ShowAsync(sender.Detail.GroupId);

        model.ToggleZoomCommand.Execute(null);

        Assert.Equal(expected, model.CanZoom);
        Assert.Equal(expected, model.IsZoomMode);
        Assert.Equal(!expected, model.IsDetailsMode);
    }

    [Fact]
    public async Task ZoomCompare_SharesOneAreaAcrossMembers_AndLeavesWithoutLosingTheKeeper()
    {
        var sender = new GroupSender(Detail(NotExact, Member(), Member()));
        var model = DuplicateTestBed.Comparison(sender);
        await model.ShowAsync(sender.Detail.GroupId);
        model.ToggleZoomCommand.Execute(null);

        model.Zoom.ZoomAt(4, 0.8, 0.3);
        model.Zoom.PanBy(0.1, 0.1);

        // One state serves every preview, so zooming one member zooms all of them to the same area.
        Assert.Equal(4, model.Zoom.Scale);
        var area = (model.Zoom.CenterX, model.Zoom.CenterY);
        Assert.NotEqual((0.5, 0.5), area);

        model.ResetZoomCommand.Execute(null);
        Assert.Equal(1, model.Zoom.Scale);
        model.LeaveZoomCommand.Execute(null);
        Assert.False(model.IsZoomMode);
    }

    [Fact]
    public async Task MoveRuleUp_SavesTheOrderPerLibrary_AndAsksForTheSuggestionAgain()
    {
        var settings = new FakeSettingsService();
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(), Member()));
        var model = DuplicateTestBed.Comparison(sender, settings);
        await model.ShowAsync(sender.Detail.GroupId);

        await model.MoveRuleUpCommand.ExecuteAsync(model.Rules[2]);

        Assert.Equal(KeeperRule.LargestFile, model.Rules[1].Rule);
        Assert.Equal(KeeperRule.HighestResolution, model.Rules[2].Rule);
        var key = Assert.Single(settings.Current.Values.Keys, key => key.StartsWith("duplicates.keeperRules.", StringComparison.Ordinal));
        Assert.Equal(JsonValueKind.Array, settings.Current.Values[key].ValueKind);
        Assert.Equal(KeeperRule.LargestFile, sender.LastOrder![1]);
    }

    [Fact]
    public async Task ResetRules_GoesBackToTheDefaultOrder_AndRemovesTheSavedValue()
    {
        var settings = new FakeSettingsService();
        var sender = new GroupSender(Detail(DuplicateGroupType.Exact, Member(), Member()));
        var model = DuplicateTestBed.Comparison(sender, settings);
        await model.ShowAsync(sender.Detail.GroupId);
        await model.MoveRuleDownCommand.ExecuteAsync(model.Rules[0]);

        await model.ResetRulesCommand.ExecuteAsync(null);

        Assert.Equal(KeeperRules.Default, model.Rules.Select(rule => rule.Rule));
        Assert.Empty(settings.Current.Values.Keys);
    }

    [Fact]
    public void Rules_LoadedFromASavedOrder_AreMadeComplete()
    {
        var settings = new FakeSettingsService();
        var session = new FakeBrowserSession();
        new KeeperRuleSettings(settings, session).Save([KeeperRule.Oldest, KeeperRule.LargestFile]);

        var loaded = new KeeperRuleSettings(settings, session).Load();

        Assert.Equal([KeeperRule.Oldest, KeeperRule.LargestFile, KeeperRule.MostUserMetadata, KeeperRule.HighestResolution, KeeperRule.OriginalFormat], loaded);
    }

    private static DuplicateGroupDetail Detail(DuplicateGroupType type, params DuplicateMemberDetail[] members) =>
        new(Id<DuplicateGroup>.New(), type, members, null);

    private static DuplicateMemberDetail Member(long size = 1000, string format = "jpg", int tags = 0, bool favorite = false, bool available = true) =>
        new(Id<MediaItem>.New(), "photo." + format, format, size, Created, Created, 4032, 3024, new DateTime(2024, 5, 1), "D:\\Photos", available, tags, 0, favorite);

    private sealed class GroupSender(DuplicateGroupDetail detail) : FakeSender
    {
        public DuplicateGroupDetail Detail { get; } = detail;

        public IReadOnlyList<KeeperRule>? LastOrder { get; private set; }

        protected override object? Handle(object request)
        {
            if (request is GetDuplicateGroupQuery query)
            {
                LastOrder = query.KeeperOrder;
                return Detail;
            }

            return base.Handle(request);
        }
    }
}
