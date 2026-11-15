using System.Diagnostics;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for re-indexing (E06-S07-T02): <see cref="AnalyzeScopeCommand"/> with the outdated and library scopes, on the
/// sample images analyzed by the real pipeline, then re-analyzed with a different fake describer model.
/// </summary>
public sealed class ReindexTests : IAsyncDisposable
{
    private readonly FakeImageDescriber describer = new(describe: (_, _) => Describe("Old"));
    private readonly FakeImageDescriber better = new(describe: (_, _) => Describe("New"), modelName: "fake-describer-2");
    private readonly FakeOcrProvider ocr = new(recognize: _ => "TEXT");
    private readonly FakeEmbeddingProvider embedding = new();
    private ImportTestBed? bed;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (bed is not null)
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task OutdatedItems_WithADifferentModel_ReplaceTheAiValuesAndKeepTheUserValues()
    {
        var items = await AnalyzedAsync(SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha, SampleMedia.WebP);
        var edited = items[0];
        await bed!.Library.Metadata.UpdateAsync(
            edited.Id,
            metadata =>
            {
                metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("My title"), DateTimeOffset.UtcNow);
                metadata.RejectTag(TagName.From("old tag"), DateTimeOffset.UtcNow);
            },
            Token);
        await bed.Sender.Send(new UpdateAnalysisProfileCommand(Profile(better)), Token);

        var result = await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(items.Count, result.ItemCount);
        Assert.Equal(items.Count, better.Calls);

        // Only the outdated describer ran again.
        Assert.Equal(items.Count, describer.Calls);
        Assert.Equal(items.Count, ocr.Calls);
        Assert.Equal(items.Count, embedding.Calls);
        foreach (var item in items)
        {
            var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
            Assert.Equal("New description", metadata.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
            Assert.Equal("TEXT", metadata.GetEffectiveValue(DescriptiveField.OcrText)?.Value.Text);
            Assert.Equal(2, metadata.Runs.Count(run => run.Analyzer == AnalyzerKeys.Describer));
        }

        var mine = await bed.Library.Metadata.GetAsync(edited.Id, Token);
        Assert.Equal("My title", mine.GetEffectiveValue(DescriptiveField.Title)?.Value.Text);
        Assert.DoesNotContain(mine.GetEffectiveTags(), tag => tag.Tag == TagName.From("old tag"));
        Assert.Contains(mine.GetEffectiveTags(), tag => tag.Tag == TagName.From("new tag"));
        Assert.Equal("New title", (await bed.Library.Metadata.GetAsync(items[1].Id, Token)).GetEffectiveValue(DescriptiveField.Title)?.Value.Text);
        Assert.Equal(0, (await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token)).Count);
    }

    [Fact]
    public async Task Interrupted_LeavesEveryItemWhollyOldOrWhollyNew_AndTheRestOutdated()
    {
        // The first item's description returns; the others wait until the job is cancelled, then return too late.
        using var release = new ManualResetEventSlim(false);
        var started = 0;
        var slow = new FakeImageDescriber(
            describe: (_, _) =>
            {
                if (Interlocked.Increment(ref started) > 1)
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                }

                return Describe("New");
            },
            modelName: "fake-describer-2");
        var items = await AnalyzedAsync([embedding, describer, slow, ocr], SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha, SampleMedia.WebP);
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(Profile(slow)), Token);

        var result = await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated), Token);
        var stopwatch = Stopwatch.StartNew();
        while (Volatile.Read(ref started) < 2)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "The re-index never reached a second item.");
            await Task.Delay(20, Token);
        }

        await bed.Jobs.Current!.CancelJobAsync(result.JobId!.Value, Token);
        release.Set();
        await bed.WaitUntilIdleAsync();

        var renewed = 0;
        foreach (var item in items)
        {
            var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
            var active = Assert.Single(metadata.Runs, run => run.Analyzer == AnalyzerKeys.Describer && run.IsActive);
            var prefix = metadata.GetEffectiveValue(DescriptiveField.Title)!.Value.Text!.Split(' ')[0];
            Assert.Equal($"{prefix} description", metadata.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
            Assert.Equal([TagName.From($"{prefix.ToLowerInvariant()} tag")], metadata.GetEffectiveTags().Select(tag => tag.Tag));
            Assert.Equal(prefix == "New" ? 2 : 1, metadata.Runs.Count(run => run.Analyzer == AnalyzerKeys.Describer));
            var states = await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token);
            Assert.Equal(active.Id, states.Single(state => state.Analyzer == AnalyzerKeys.Describer).RunId);
            renewed += prefix == "New" ? 1 : 0;
        }

        Assert.InRange(renewed, 1, items.Count - 1);
        Assert.Equal(items.Count - renewed, (await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token)).Count);
    }

    [Fact]
    public async Task Reanalysis_WithAModelThatChangesSomeValues_FlagsExactlyThoseFieldsAndTags()
    {
        // The new model keeps the title and summary, changes the description and swaps "old tag" for "extra tag".
        var partial = new FakeImageDescriber(
            describe: (_, _) => new ImageDescription("Old title", "Changed description", "Old summary", ["extra tag"], [], [], [], []),
            modelName: "fake-describer-3");
        var items = await AnalyzedAsync([embedding, describer, partial, ocr], SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha);
        var owned = items[0];
        await bed!.Library.Metadata.UpdateAsync(owned.Id, metadata => metadata.AcceptAll(DateTimeOffset.UtcNow), Token);
        await bed.Sender.Send(new UpdateAnalysisProfileCommand(Profile(partial)), Token);

        await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated), Token);
        await bed.WaitUntilIdleAsync();

        var mine = await bed.Library.Metadata.GetAsync(owned.Id, Token);
        Assert.Equal([DescriptiveField.Description], mine.UserValues.Where(value => value.HasNewAiSuggestion).Select(value => value.Field));
        Assert.Equal("Old description", mine.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
        Assert.Equal(
            [("extra tag", TagSuggestionKind.Added), ("old tag", TagSuggestionKind.Removed)],
            mine.TagSuggestions.Select(suggestion => (suggestion.Tag.Value, suggestion.Kind)).Order());
        Assert.True(mine.HasNewAiSuggestions);

        // Without user values, the new AI values just become effective; "extra tag" is still new to the item.
        var other = await bed.Library.Metadata.GetAsync(items[1].Id, Token);
        Assert.Empty(other.UserValues);
        Assert.Equal("Changed description", other.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
        Assert.Equal([("extra tag", TagSuggestionKind.Added)], other.TagSuggestions.Select(suggestion => (suggestion.Tag.Value, suggestion.Kind)));
    }

    [Fact]
    public async Task Filters_OutdatedAndNewSuggestions_ReturnExactlyTheFlaggedItems()
    {
        var partial = new FakeImageDescriber(
            describe: (_, _) => new ImageDescription("Old title", "Changed description", "Old summary", ["extra tag"], [], [], [], []),
            modelName: "fake-describer-3");
        var items = await AnalyzedAsync([embedding, describer, partial, ocr], true, SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha, SampleMedia.WebP);
        await bed!.Library.Metadata.UpdateAsync(items[0].Id, metadata => metadata.AcceptAll(DateTimeOffset.UtcNow), Token);
        await bed.Library.Metadata.UpdateAsync(items[2].Id, metadata => metadata.AcceptAll(DateTimeOffset.UtcNow), Token);
        await bed.Sender.Send(new UpdateAnalysisProfileCommand(Profile(partial)), Token);

        // Before: every item is outdated and none has a suggestion.
        Assert.Equal(Ids(items), await FilteredAsync(OutdatedAnalysisFilter.FilterId, true));
        Assert.Empty(await FilteredAsync(NewSuggestionsFilter.FilterId, true));

        // Re-index the first two only: the third stays outdated.
        await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Items([items[0].Id, items[1].Id])), Token);
        await bed.WaitUntilIdleAsync();

        var outdated = await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token);
        Assert.Equal([items[2].Id], outdated.ItemIds);
        Assert.Equal(Ids(outdated.ItemIds), await FilteredAsync(OutdatedAnalysisFilter.FilterId, true));
        Assert.Equal(Ids([items[0].Id, items[1].Id]), await FilteredAsync(OutdatedAnalysisFilter.FilterId, false));

        var flagged = new List<Id<MediaItem>>();
        foreach (var item in items)
        {
            if ((await bed.Library.Metadata.GetAsync(item.Id, Token)).HasNewAiSuggestions)
            {
                flagged.Add(item.Id);
            }
        }

        Assert.Equal(Ids([items[0].Id, items[1].Id]), Ids(flagged));
        Assert.Equal(Ids(flagged), await FilteredAsync(NewSuggestionsFilter.FilterId, true));
        Assert.Equal([items[2].Id], await FilteredAsync(NewSuggestionsFilter.FilterId, false));

        // Dismissing every suggestion of the first item clears it from the filter.
        await bed.Library.Metadata.UpdateAsync(
            items[0].Id,
            metadata =>
            {
                metadata.DismissSuggestion(DescriptiveField.Description);
                foreach (var tag in metadata.TagSuggestions.Select(suggestion => suggestion.Tag).ToList())
                {
                    metadata.DismissTagSuggestion(tag);
                }
            },
            Token);
        Assert.Equal([items[1].Id], await FilteredAsync(NewSuggestionsFilter.FilterId, true));
    }

    [Fact]
    public async Task OutdatedScope_NothingOutdated_QueuesNothing()
    {
        await AnalyzedAsync(SampleMedia.JpegWithExif);

        var result = await bed!.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated), Token);

        Assert.Null(result.JobId);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task OutdatedScope_WithOtherAnalyzersChosen_FindsNothing()
    {
        await AnalyzedAsync(SampleMedia.JpegWithExif);
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(Profile(better)), Token);

        var result = await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated, [AnalyzerKeys.Ocr]), Token);

        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task LibraryScope_WithChosenAnalyzers_RunsThemOnEveryItem()
    {
        var items = await AnalyzedAsync(SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha);

        var result = await bed!.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Library, [AnalyzerKeys.Ocr]), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(items.Count, result.ItemCount);
        Assert.Equal(2 * items.Count, ocr.Calls);
        Assert.Equal(items.Count, describer.Calls);
    }

    private static List<Id<MediaItem>> Ids(IEnumerable<MediaItem> items) => Ids(items.Select(item => item.Id));

    private static List<Id<MediaItem>> Ids(IEnumerable<Id<MediaItem>> ids) => [.. ids.OrderBy(id => id.Value)];

    private async Task<List<Id<MediaItem>>> FilteredAsync(string filterId, bool value)
    {
        var results = await bed!.Sender.Send(new SearchMediaQuery { Filters = FilterState.Empty.Set(filterId, new BooleanValue(value)) }, Token);
        return Ids(results.Hits.Select(hit => hit.ItemId));
    }

    private static ImageDescription Describe(string prefix) =>
        new($"{prefix} title", $"{prefix} description", $"{prefix} summary", [$"{prefix.ToLowerInvariant()} tag"], [], [], [], []);

    private static AnalysisProfile Profile(FakeImageDescriber chosen) =>
        AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ModelId = chosen.Model.ModelName, MaxTags = AnalysisProfile.DefaultMaxTags });

    private Task<IReadOnlyList<MediaItem>> AnalyzedAsync(params string[] files) => AnalyzedAsync([embedding, describer, better, ocr], files);

    private Task<IReadOnlyList<MediaItem>> AnalyzedAsync(IAiProvider[] providers, params string[] files) => AnalyzedAsync(providers, false, files);

    private async Task<IReadOnlyList<MediaItem>> AnalyzedAsync(IAiProvider[] providers, bool search, params string[] files)
    {
        bed = await ImportTestBed.CreateAsync(configure: services =>
        {
            services.AddSingleton<IAiProviderRegistry>(new AiProviderRegistry(providers, TimeProvider.System));
            if (search)
            {
                services.AddSearch();
                services.AddSingleton<IFilterDefinition, OutdatedAnalysisFilter>();
                services.AddSingleton<IFilterDefinition, NewSuggestionsFilter>();
            }
        });
        if (search)
        {
            await bed.Get<SearchIndex>().Ready;
        }

        foreach (var file in files)
        {
            bed.Copy(file, Path.GetFileName(file));
        }

        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var items = await bed.ItemsAsync();
        await bed.Sender.Send(new AnalyzeItemsCommand([.. items.Select(item => item.Id)]), Token);
        await bed.WaitUntilIdleAsync();
        return items;
    }
}
