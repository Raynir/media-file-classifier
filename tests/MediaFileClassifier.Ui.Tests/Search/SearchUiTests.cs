using System.Globalization;
using System.Resources;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Search;

/// <summary>
/// Tests for the search box, the notice above the results and the match reasons (E09-S01-T04, T05).
/// </summary>
public sealed class SearchUiTests
{
    private readonly Localizer localizer = new(new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
    };

    [Theory]
    [InlineData(SearchFields.Tags, false, false, "Matched in: Tags")]
    [InlineData(SearchFields.OcrText | SearchFields.Title, false, false, "Matched in: Title, Text in the image")]
    [InlineData(SearchFields.FileName, true, false, "Matched in: File name · Visual match")]
    [InlineData(SearchFields.None, true, false, "Visual match")]
    [InlineData(SearchFields.None, true, true, "Visual match · Less relevant")]
    [InlineData(SearchFields.None, false, false, null)]
    public void MatchReason_NamesTheFieldsAndTheVisualMatch(SearchFields fields, bool semantic, bool lessRelevant, string? expected)
    {
        Assert.Equal(expected, MatchReasonText.For(new ResultMatch(fields, semantic, lessRelevant), localizer));
    }

    [Fact]
    public void MatchReason_WhileBrowsing_IsNothing() => Assert.Null(MatchReasonText.For(null, localizer));

    [Fact]
    public Task Notice_FollowsTheSearch_AndTogglesTheLessRelevantResults() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var notice = new SearchNoticeViewModel(source, localizer);
        Assert.False(notice.IsVisible);

        sender.Total = 3;
        sender.LessRelevant = 12;
        sender.Semantic = SemanticSearchStatus.ModelMissing;
        source.SetText("sign");
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.IsVisible);

        Assert.Equal("3 results", notice.Message);
        Assert.Equal("Show 12 less relevant results", notice.LessRelevantText);
        Assert.Contains("only text matches", notice.Hint, StringComparison.Ordinal);

        notice.ToggleLessRelevantCommand.Execute(null);
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.LessRelevantText == "Hide less relevant results");
        Assert.True(sender.LastQuery!.ShowLessRelevant);

        source.SetText(string.Empty);
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => !notice.IsVisible);
    });

    [Fact]
    public Task Notice_NoResultsAndNothingLessRelevant_HidesTheOption() => TestApp.RunAsync(() =>
    {
        var sender = new StubSender();
        var source = CreateSource(sender);
        var notice = new SearchNoticeViewModel(source, localizer);

        source.SetText("zzzz");
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.IsVisible);

        Assert.Equal("No results", notice.Message);
        Assert.Null(notice.LessRelevantText);
        Assert.Null(notice.Hint);
    });

    [Fact]
    public Task SearchBar_DoesNotSearchWhileTyping_SearchesOnEnter_AndClearGoesBackToBrowsing() => TestApp.RunAsync(() =>
    {
        var source = CreateSource(new StubSender());
        var bar = new SearchBarViewModel(source, TimeSpan.FromMilliseconds(30));

        bar.Text = "bea";
        bar.Text = "beach";

        // Typing alone does not search: the pause timer does it, and Enter does it at once.
        Assert.Equal(string.Empty, source.Text);

        bar.Text = "lake";
        bar.SubmitCommand.Execute(null);
        Assert.Equal("lake", source.Text);

        Assert.True(bar.ClearCommand.CanExecute(null));
        bar.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal(string.Empty, source.Text);
        Assert.False(bar.ClearCommand.CanExecute(null));
    });

    [Fact]
    public Task SearchBar_FindSimilar_ShowsTheChip_TextReplacesIt_AndClearingLeavesIt() => TestApp.RunAsync(() =>
    {
        var source = CreateSource(new StubSender());
        var bar = new SearchBarViewModel(source, TimeSpan.FromMilliseconds(30));
        var item = new SimilarSource(Id<MediaItem>.New(), "IMG_1234.jpg");
        bar.Text = "beach";

        source.SetSimilarTo(item);

        Assert.True(bar.IsSimilarMode);
        Assert.Equal("Similar to: IMG_1234.jpg", bar.SimilarLabel);
        Assert.Equal(string.Empty, bar.Text);
        Assert.Equal(item, source.SimilarTo);

        // The pause after emptying the box doesn't leave the mode; text typed replaces it.
        var waitUntil = DateTime.UtcNow.AddMilliseconds(100);
        Pump.Until(() => DateTime.UtcNow > waitUntil);
        Assert.True(bar.IsSimilarMode);
        bar.Text = "lake";
        bar.SubmitCommand.Execute(null);
        Assert.False(bar.IsSimilarMode);
        Assert.Null(source.SimilarTo);
        Assert.Equal("lake", source.Text);

        // Escape with an empty box, or the chip's clear button, goes back to browsing.
        source.SetSimilarTo(item);
        Assert.True(bar.ClearCommand.CanExecute(null));
        bar.ClearCommand.Execute(null);
        Assert.False(bar.IsSimilarMode);
        Assert.False(source.SimilarTo is not null || source.Text.Length > 0);

        source.SetSimilarTo(item);
        bar.ClearSimilarCommand.Execute(null);
        Assert.Null(bar.SimilarLabel);
        Assert.Null(source.SimilarTo);
    });

    [Theory]
    [InlineData(SemanticSearchStatus.NotAnalyzed, true)]
    [InlineData(SemanticSearchStatus.Used, false)]
    public Task SearchBar_FindSimilar_OffersAnalyzeNow_OnlyForAnUnanalyzedItem(SemanticSearchStatus semantic, bool offered) => TestApp.RunAsync(() =>
    {
        var sender = new StubSender { Semantic = semantic };
        var source = CreateSource(sender);
        var analyzeNow = new AnalyzeNow(source, sender, new FakeBrowserSession(open: false));
        var bar = new SearchBarViewModel(source, TimeSpan.FromMilliseconds(30), analyzeNow: analyzeNow);

        source.SetSimilarTo(new SimilarSource(Id<MediaItem>.New(), "IMG_1.jpg"));
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => source.Summary is not null);
        Pump.Until(() => bar.IsAnalyzeNowOffered == offered);

        Assert.Equal(offered, bar.IsAnalyzeNowOffered);
        Assert.Equal(offered, bar.AnalyzeNowCommand.CanExecute(null));
        Assert.False(bar.HasAnalyzeStatus);

        // Leaving the mode hides it.
        bar.ClearSimilarCommand.Execute(null);
        Pump.Until(() => !bar.IsAnalyzeNowOffered);
        Assert.False(bar.IsAnalyzeNowOffered);
    });

    [Theory]
    [InlineData(SemanticSearchStatus.Used, 0, "No similar items", null)]
    [InlineData(SemanticSearchStatus.Used, 1, "1 similar item", null)]
    [InlineData(SemanticSearchStatus.Used, 5, "5 similar items", null)]
    [InlineData(SemanticSearchStatus.NotAnalyzed, 0, "No similar items", "hasn't been analyzed")]
    public Task Notice_FindSimilar_CountsSimilarItems(SemanticSearchStatus semantic, int total, string message, string? hint) => TestApp.RunAsync(() =>
    {
        var sender = new StubSender { Total = total, Semantic = semantic };
        var source = CreateSource(sender);
        var notice = new SearchNoticeViewModel(source, localizer);

        source.SetSimilarTo(new SimilarSource(Id<MediaItem>.New(), "IMG_1.jpg"));
        _ = source.CountAsync(CancellationToken.None);
        Pump.Until(() => notice.IsVisible);

        Assert.Equal(message, notice.Message);
        if (hint is null)
        {
            Assert.Null(notice.Hint);
        }
        else
        {
            Assert.Contains(hint, notice.Hint, StringComparison.Ordinal);
        }
    });

    [Fact]
    public void MatchReason_ForTheSource_SaysSo() =>
        Assert.Equal("The item you're finding similar items to", MatchReasonText.For(new ResultMatch(SearchFields.None, false, false, IsSource: true), localizer));

    private static SearchResultSource CreateSource(StubSender sender) =>
        new(new LibraryItemsSource(sender, new FakeBrowserSession(open: false), TimeProvider.System), sender);

    private sealed class StubSender : ISender
    {
        public int Total { get; set; }

        public int LessRelevant { get; set; }

        public SemanticSearchStatus Semantic { get; set; } = SemanticSearchStatus.Used;

        public SearchMediaQuery? LastQuery { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is FindSimilarQuery)
            {
                var status = Semantic == SemanticSearchStatus.Used ? Semantic : SemanticSearchStatus.NotAnalyzed;
                return Task.FromResult((TResponse)(object)new MediaSearchResults([], Total, [], SearchMode.Similar, status));
            }

            if (request is SearchMediaQuery query)
            {
                LastQuery = query;
                return Task.FromResult((TResponse)(object)new MediaSearchResults([], Total, [], SearchMode.Hybrid, Semantic, LessRelevant));
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
