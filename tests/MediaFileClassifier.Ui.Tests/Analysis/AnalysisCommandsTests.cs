using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Analysis;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Browsing.Selection;

namespace MediaFileClassifier.Ui.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalysisCommands"/> and <see cref="AnalysisRunner"/>: the Analyze entries for the selection,
/// the Library entries, the question before a large run, and the offer when automatic analysis is switched on
/// (E06-S06-T01, T02).
/// </summary>
public sealed class AnalysisCommandsTests
{
    [Fact]
    public async Task ItemMenu_OffersAnalyzeAndTheOnlyWithSubmenu_ForASelection()
    {
        var bed = new Bed();
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        var entries = bed.Menu.Build();

        Assert.Contains(entries.OfType<AppCommand>(), command => command.Id == AnalysisCommands.Analyze);
        var only = Assert.Single(entries.OfType<MenuSubmenu>());
        Assert.Equal(
            [AnalysisCommands.AnalyzeEmbedding, AnalysisCommands.AnalyzeDescriber, AnalysisCommands.AnalyzeOcr],
            only.Items.OfType<AppCommand>().Select(command => command.Id));
    }

    [Fact]
    public void ItemMenu_HasNoAnalyzeEntries_WithoutASelection()
    {
        var bed = new Bed();

        Assert.Empty(bed.Menu.Build());
    }

    [Fact]
    public async Task Analyze_QueuesTheSelectedItems_WithTheWholeProfile()
    {
        var bed = new Bed();
        await bed.Selection.ApplyAsync(2, SelectionGesture.Replace);
        await bed.Selection.ApplyAsync(4, SelectionGesture.Toggle);

        await bed.RunAsync(AnalysisCommands.Analyze);

        var command = Assert.Single(bed.Queued);
        Assert.Null(command.Analyzers);
        Assert.Equal(new HashSet<Id<MediaItem>> { bed.Source.Ids[2], bed.Source.Ids[4] }, ((AnalysisScope.ItemsScope)command.Scope).ItemIds.ToHashSet());
        Assert.Empty(bed.Dialogs.Questions);
    }

    [Theory]
    [InlineData(AnalysisCommands.AnalyzeEmbedding, AnalyzerKeys.Embedding)]
    [InlineData(AnalysisCommands.AnalyzeDescriber, AnalyzerKeys.Describer)]
    [InlineData(AnalysisCommands.AnalyzeOcr, AnalyzerKeys.Ocr)]
    public async Task AnalyzeOnly_QueuesTheSelection_WithThatAnalyzer(string commandId, string analyzer)
    {
        var bed = new Bed();
        await bed.Selection.ApplyAsync(3, SelectionGesture.Replace);

        await bed.RunAsync(commandId);

        Assert.Equal([analyzer], Assert.Single(bed.Queued).Analyzers);
    }

    [Fact]
    public async Task AnalyzePending_QueuesThePendingItems_UnderTheQuestionThreshold()
    {
        var bed = new Bed { Resolved = Items(3) };

        await bed.RunAsync(AnalysisCommands.AnalyzePending);

        var command = Assert.Single(bed.Queued);
        Assert.Equal(3, ((AnalysisScope.ItemsScope)command.Scope).ItemIds.Count);
        Assert.Equal("Analyze 3 pending items", bed.Descriptions.Single());
        Assert.Empty(bed.Dialogs.Questions);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task AnalyzeResults_AsksBeforeALargeRun_AndQueuesOnlyWhenConfirmed(bool confirm, int queued)
    {
        var bed = new Bed { Resolved = Items(AnalysisRunner.ConfirmFrom) };
        bed.Dialogs.Answer = confirm;

        await bed.RunAsync(AnalysisCommands.AnalyzeResults);

        Assert.Equal(queued, bed.Queued.Count);
        var question = Assert.Single(bed.Dialogs.Questions);
        Assert.Contains("500 items", question);
    }

    [Fact]
    public async Task AnalyzeResults_ShowsTheEstimate_WhenARateIsKnown()
    {
        var bed = new Bed { Resolved = Items(600), Rate = 10 };

        await bed.RunAsync(AnalysisCommands.AnalyzeResults);

        Assert.Contains("1 min", Assert.Single(bed.Dialogs.Questions));
    }

    [Fact]
    public async Task AnalyzeResults_ShowsTheCountOnly_WhenNoRateIsKnown()
    {
        var bed = new Bed { Resolved = Items(600), Rate = null };

        await bed.RunAsync(AnalysisCommands.AnalyzeResults);

        Assert.DoesNotContain("about", Assert.Single(bed.Dialogs.Questions));
    }

    [Fact]
    public async Task Analyze_TellsTheUser_WhenThereIsNothingToAnalyze()
    {
        var bed = new Bed { Resolved = Items(0) };

        await bed.RunAsync(AnalysisCommands.AnalyzePending);

        Assert.Empty(bed.Queued);
        Assert.Equal(["There is nothing to analyze."], bed.Dialogs.Notices);
    }

    [Fact]
    public async Task Analyze_TellsTheUser_WhenBackgroundProcessingIsNotRunning()
    {
        var bed = new Bed { Resolved = Items(2), JobQueued = false };

        await bed.RunAsync(AnalysisCommands.AnalyzePending);

        Assert.Equal(["Background processing isn't running, so nothing was queued."], bed.Dialogs.Notices);
    }

    [Fact]
    public async Task OfferPending_QueuesTheBacklog_WithoutAskingAgain_WhenAccepted()
    {
        var bed = new Bed { PendingCount = 600, Resolved = Items(600) };

        await bed.Commands.OfferPendingAsync();

        var question = Assert.Single(bed.Dialogs.Questions);
        Assert.Contains("600 items", question);
        Assert.Equal(600, ((AnalysisScope.ItemsScope)Assert.Single(bed.Queued).Scope).ItemIds.Count);
    }

    [Fact]
    public async Task OfferPending_QueuesNothing_WhenDeclined()
    {
        var bed = new Bed { PendingCount = 4, Resolved = Items(4) };
        bed.Dialogs.Answer = false;

        await bed.Commands.OfferPendingAsync();

        Assert.Single(bed.Dialogs.Questions);
        Assert.Empty(bed.Queued);
    }

    [Fact]
    public async Task OfferPending_SaysNothing_WhenNothingIsPending()
    {
        var bed = new Bed { PendingCount = 0 };

        await bed.Commands.OfferPendingAsync();

        Assert.Empty(bed.Dialogs.Questions);
        Assert.Empty(bed.Queued);
    }

    private static AnalysisScopeItems Items(int count) =>
        new([.. Enumerable.Range(0, count).Select(_ => Id<MediaItem>.New())], count, 0);

    private sealed class Bed
    {
        public Bed()
        {
            Source = new FakeResultSource(10);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(10);
            var sender = new BedSender(this);
            Commands = new AnalysisCommands(sender, new AnalysisRunner(sender, Dialogs, Localizer.Current), Selection, Dialogs, Localizer.Current, session: new FakeBrowserSession());
            Registry = new CommandRegistry([Commands], Localizer.Current, new FakeCapabilityGate());
            Menu = new BrowserContextMenu(Registry, Localizer.Current);
        }

        public Task RunAsync(string commandId)
        {
            Registry.Refresh();
            return Registry[commandId].ExecuteAsync();
        }

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public FakeAnalysisDialogs Dialogs { get; } = new();

        public AnalysisCommands Commands { get; }

        public CommandRegistry Registry { get; }

        public BrowserContextMenu Menu { get; }

        public List<AnalyzeScopeCommand> Queued { get; } = [];

        public List<string?> Descriptions { get; } = [];

        public AnalysisScopeItems Resolved { get; set; } = AnalysisScopeItems.Empty;

        public int PendingCount { get; set; }

        public double? Rate { get; set; }

        public bool JobQueued { get; set; } = true;

        private sealed class BedSender(Bed bed) : FakeSender
        {
            protected override object? Handle(object request)
            {
                switch (request)
                {
                    case ResolveItemSelectionQuery query:
                        return (IReadOnlyList<Id<MediaItem>>)[.. bed.Source.Ids.Where(query.Selection.Contains)];
                    case ResolveAnalysisScopeQuery { Scope: AnalysisScope.ItemsScope chosen }:
                        return new AnalysisScopeItems([.. chosen.ItemIds], chosen.ItemIds.Count, 0);
                    case ResolveAnalysisScopeQuery:
                        return bed.Resolved;
                    case GetAnalysisEstimateQuery estimate:
                        return GetAnalysisEstimateQueryHandler.Estimate(bed.Rate, estimate.Images, estimate.VideoFrames);
                    case CountLibraryItemsQuery:
                        return bed.PendingCount;
                    case AnalyzeScopeCommand command:
                        bed.Queued.Add(command);
                        bed.Descriptions.Add(command.Description);
                        return new AnalyzeScopeResult(bed.JobQueued ? Id<ProcessingJob>.New() : null, ((AnalysisScope.ItemsScope)command.Scope).ItemIds.Count);
                    default:
                        throw new NotSupportedException(request.GetType().Name);
                }
            }
        }
    }

    private sealed class FakeAnalysisDialogs : IAnalysisDialogs
    {
        public bool Answer { get; set; } = true;

        public List<string> Questions { get; } = [];

        public List<string> Notices { get; } = [];

        public Task<bool> ConfirmAsync(string message, string confirmLabel, string cancelLabel)
        {
            Questions.Add(message);
            return Task.FromResult(Answer);
        }

        public Task ShowAsync(string message)
        {
            Notices.Add(message);
            return Task.CompletedTask;
        }
    }
}
