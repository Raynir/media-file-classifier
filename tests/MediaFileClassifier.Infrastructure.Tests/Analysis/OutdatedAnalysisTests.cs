using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for outdated detection (E06-S07-T01): <see cref="GetOutdatedAnalysisQuery"/> and <see cref="CurrentProvenance"/>
/// on items analyzed by the real pipeline with fake providers.
/// </summary>
public sealed class OutdatedAnalysisTests : IAsyncDisposable
{
    private readonly FakeImageDescriber describer = new();
    private readonly FakeImageDescriber better = new(modelName: "fake-describer-2");
    private readonly FakeOcrProvider ocr = new();
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
    public async Task ChangingTheDescriberModel_ImmediatelyShowsEveryAnalyzedItemAsOutdated()
    {
        var items = await AnalyzedAsync(SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha);
        Assert.Equal(0, (await OutdatedAsync()).Count);

        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(Profile(better)), Token);

        var outdated = await OutdatedAsync();
        Assert.Equivalent(items.Select(item => item.Id), outdated.ItemIds, strict: true);
        Assert.Equal([AnalyzerKeys.Describer], outdated.Analyzers);
    }

    [Fact]
    public async Task ProposedProfile_CountsWithoutSavingIt_AndTheSavedProfileStaysCurrent()
    {
        await AnalyzedAsync(SampleMedia.JpegWithExif);

        var proposed = await bed!.Sender.Send(new GetOutdatedAnalysisQuery(Profile(better)), Token);

        Assert.Equal(1, proposed.Count);
        Assert.Equal(0, (await OutdatedAsync()).Count);
    }

    [Fact]
    public async Task ChangingBack_ClearsTheOutdatedItems()
    {
        await AnalyzedAsync(SampleMedia.JpegWithExif);
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(Profile(better)), Token);

        await bed.Sender.Send(new UpdateAnalysisProfileCommand(Profile(describer)), Token);

        Assert.Equal(0, (await OutdatedAsync()).Count);
    }

    [Fact]
    public async Task ChangingThePromptTemplate_ShowsTheItemsAsOutdated_UntilTheyAreDescribedAgain()
    {
        var items = await AnalyzedAsync(SampleMedia.JpegWithExif, SampleMedia.PngWithAlpha);
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ModelId = describer.Model.ModelName, PromptVersion = "fake-2" });

        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(profile), Token);

        var outdated = await OutdatedAsync();
        Assert.Equivalent(items.Select(item => item.Id), outdated.ItemIds, strict: true);
        Assert.Equal([AnalyzerKeys.Describer], outdated.Analyzers);

        await bed.Sender.Send(new AnalyzeItemsCommand([.. outdated.ItemIds], [AnalyzerKeys.Describer]), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(0, (await OutdatedAsync()).Count);
        var metadata = await bed.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal("fake-2", metadata.Runs.Single(run => run.Analyzer == AnalyzerKeys.Describer && run.IsActive).PromptVersion);
    }

    [Fact]
    public async Task ANewShippedTemplateVersion_ShowsTheItemsAsOutdated()
    {
        await AnalyzedAsync(SampleMedia.JpegWithExif);
        var stored = (await bed!.Library.Metadata.GetAsync((await bed.ItemsAsync())[0].Id, Token)).Runs.Single(run => run.Analyzer == AnalyzerKeys.Describer);
        var model = Assert.Single(await bed.Library.Models.GetAllAsync(Token), model => model.Id == stored.ModelId);

        // The same describer after an app update whose default template is newer.
        var updated = new AiProviderRegistry([new FakeImageDescriber(promptVersion: "fake-2")], TimeProvider.System);
        var current = Assert.Single(CurrentProvenance.Resolve(AnalysisProfile.Default, updated));

        Assert.Equal("fake-2", current.PromptVersion);
        Assert.False(current.Matches(model, stored.PromptVersion));
    }

    [Fact]
    public void Resolve_LeavesOutDisabledAnalyzersAndThoseWithoutAProvider()
    {
        var registry = new AiProviderRegistry([describer, better, embedding], TimeProvider.System);
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Embedding) { Enabled = false });

        var current = CurrentProvenance.Resolve(profile, registry);

        // The automatic pick is the first describer; OCR has no provider.
        var only = Assert.Single(current);
        Assert.Equal(new AnalyzerProvenance(AnalyzerKeys.Describer, ProviderIds.Fake, FakeImageDescriber.DefaultModelName, "1", "fake-1"), only);
    }

    [Fact]
    public void Resolve_ChosenModel_IsTheCurrentOne()
    {
        var registry = new AiProviderRegistry([describer, better, ocr], TimeProvider.System);

        var current = CurrentProvenance.Resolve(Profile(better), registry);

        Assert.Equal("fake-describer-2", current.Single(provenance => provenance.Analyzer == AnalyzerKeys.Describer).ModelName);
        Assert.Null(current.Single(provenance => provenance.Analyzer == AnalyzerKeys.Ocr).PromptVersion);
    }

    private static AnalysisProfile Profile(FakeImageDescriber chosen) =>
        AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ModelId = chosen.Model.ModelName, MaxTags = AnalysisProfile.DefaultMaxTags });

    private Task<OutdatedAnalysis> OutdatedAsync() => bed!.Sender.Send(new GetOutdatedAnalysisQuery(), Token);

    private async Task<IReadOnlyList<MediaItem>> AnalyzedAsync(params string[] files)
    {
        bed = await ImportTestBed.CreateAsync(configure: services =>
            services.AddSingleton<IAiProviderRegistry>(new AiProviderRegistry([embedding, describer, better, ocr], TimeProvider.System)));
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
