using System.Globalization;
using System.Resources;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Search;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
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
