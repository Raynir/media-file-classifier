using System.Diagnostics;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
using MediaFileClassifier.UseCases.Analysis.GetDescriberFields;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediaFileClassifier.UseCases.Import.RetryFailedItems;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for the analysis pipeline (E06-S01-T03 to T05): items imported into a real library and analyzed by the real
/// scheduler with fake providers, so no model is needed. Success, partial failure, cancellation and unavailable providers.
/// </summary>
public sealed class AnalysisPipelineTests : IAsyncDisposable
{
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
    public async Task FullProfile_OnTheSampleImages_StoresEveryResultWithProvenance()
    {
        var describer = new FakeImageDescriber(describe: (_, _) => new ImageDescription(
            "A title",
            "A description.",
            "A summary.",
            [.. Enumerable.Range(0, 30).Select(index => $"tag {index}")],
            ["dog"],
            ["beach"],
            ["running"],
            ["joy"]));
        var ocr = new FakeOcrProvider(recognize: _ => "HELLO");
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), describer, ocr],
            SampleMedia.JpegWithExif,
            SampleMedia.JpegWithIccAndOffset,
            SampleMedia.PngWithAlpha,
            SampleMedia.WebP,
            SampleMedia.StaticGif,
            SampleMedia.ImageWithText);

        await AnalyzeAsync(items);

        foreach (var item in items)
        {
            var stored = await bed!.Library.Items.GetAsync(item.Id, Token);
            Assert.Equal(MediaItemStatus.Indexed, stored!.Status);
            var states = await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token);
            Assert.Equal(3, states.Count);
            Assert.All(states, state => Assert.Equal(AnalyzerStatus.Done, state.Status));
            Assert.Single(await bed.Library.Embeddings.GetAsync(item.Id, Token));

            var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
            var describerRun = Assert.Single(metadata.Runs, run => run.Analyzer == AnalyzerKeys.Describer);
            Assert.Equal(describer.Model.ProviderId, describerRun.ProviderId);
            Assert.Equal("fake-1", describerRun.PromptVersion);
            Assert.Equal(describerRun.Id, states.Single(state => state.Analyzer == AnalyzerKeys.Describer).RunId);
            Assert.Equal("A title", metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text);
            Assert.Equal(["dog"], metadata.GetEffectiveValue(DescriptiveField.Objects)?.Value.Items);
            Assert.Equal(AnalysisProfile.DefaultMaxTags, metadata.GetEffectiveTags().Count);
            Assert.Equal("HELLO", metadata.GetEffectiveValue(DescriptiveField.OcrText)?.Value.Text);
        }

        Assert.Equal(items.Count, describer.Calls);
        var models = await bed!.Library.Models.GetAllAsync(Token);
        Assert.Contains(models, model => describer.Model.Matches(model));
        Assert.Contains(models, model => ocr.Model.Matches(model));
    }

    [Fact]
    public async Task Describer_NormalisesTags_BeforeStoringThem()
    {
        var describer = new FakeImageDescriber(describe: (_, _) => new ImageDescription(
            "A title",
            "A description.",
            "A summary.",
            ["Dogs", "dog", " Dog ", "photo", "Beaches"],
            [],
            [],
            [],
            []));
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer], SampleMedia.JpegWithExif);

        await AnalyzeAsync(items);

        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal(["dog", "beach"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
    }

    [Fact]
    public async Task Describer_StoresOnlyTheFieldsItDeclares()
    {
        var describer = new FakeImageDescriber(
            describe: (_, _) => FullDescription("A title"),
            supportedFields: DescriberFields.Description | DescriberFields.Tags | DescriberFields.Objects);
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer], SampleMedia.JpegWithExif);

        await AnalyzeAsync(items);

        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal(
            [DescriptiveField.Description, DescriptiveField.Objects],
            metadata.GetEffectiveValues().Where(value => value.Source == MetadataSource.AI).Select(value => value.Field).Order());
        Assert.Equal(["dog"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
    }

    [Fact]
    public async Task SwitchingDescriber_ChangesWhichFieldsArePopulated()
    {
        var full = new FakeImageDescriber(describe: (_, _) => FullDescription("Full"), modelName: "full");
        var captioner = new FakeImageDescriber(
            describe: (_, _) => FullDescription("Captioner"),
            modelName: "captioner",
            supportedFields: DescriberFields.Description | DescriberFields.Objects);
        var items = await ImportAsync([new FakeEmbeddingProvider(), full, captioner], SampleMedia.JpegWithExif);
        await UseDescriberAsync(full);
        await AnalyzeAsync(items);

        await UseDescriberAsync(captioner);
        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(items[0], AnalyzerKeys.Describer)).Status);
        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal(
            [DescriptiveField.Description, DescriptiveField.Objects],
            metadata.GetEffectiveValues().Where(value => value.Source == MetadataSource.AI).Select(value => value.Field).Order());
        Assert.Equal("Captioner description.", metadata.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
        Assert.Empty(metadata.GetEffectiveTags());
        Assert.Equal(DescriberFields.Description | DescriberFields.Objects, await bed.Sender.Send(new GetDescriberFieldsQuery(), Token));
        var choices = (await bed.Sender.Send(new GetAnalysisProfileQuery(), Token))!.Analyzers.Single(choice => choice.Analyzer == AnalyzerKeys.Describer);
        Assert.Equal(DescriberFields.All, choices.Providers.Single(provider => provider.Id == full.Id).DescriberFields);
    }

    [Fact]
    public async Task Animation_WithASingleImageDescriber_IsDescribedFrameByFrame_AndMerged()
    {
        var describer = new FakeImageDescriber(describe: (image, _) => new ImageDescription(
            $"Title of {image.Name}",
            $"Description of {image.Name}.",
            $"Summary of {image.Name}.",
            ["Dogs", $"only {image.Name}"],
            ["ball"],
            [],
            [],
            []));
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer], SampleMedia.AnimatedGif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        // Several keyframes, at most four described; the texts are one frame's, the lists and tags merged.
        Assert.InRange(describer.Calls, 2, 4);
        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.StartsWith("Title of frame ", metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text, StringComparison.Ordinal);
        Assert.Equal(["ball"], metadata.GetEffectiveValue(DescriptiveField.Objects)?.Value.Items);
        var tags = metadata.GetEffectiveTags().Select(tag => tag.Tag.Value).ToList();
        Assert.Equal("dog", tags[0]);
        Assert.Equal(describer.Calls + 1, tags.Count);
    }

    [Fact]
    public async Task Animation_WithAMultiImageDescriber_IsDescribedInOneRequest()
    {
        var describer = new FakeMultiImageDescriber(maxImages: 3);
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer], SampleMedia.AnimatedGif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        var (images, options) = Assert.Single(describer.Requests);
        Assert.InRange(images.Count, 2, 3);
        Assert.All(images, image => Assert.True(Math.Max(image.PixelWidth ?? 0, image.PixelHeight ?? 0) <= 512));
        Assert.Contains("frames of one video or animation", options.Context, StringComparison.Ordinal);
        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal($"Clip of {images.Count}", metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text);
    }

    [Fact]
    public async Task StillImage_WithAMultiImageDescriber_IsSentAlone()
    {
        var describer = new FakeMultiImageDescriber();
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer], SampleMedia.JpegWithExif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        var (images, options) = Assert.Single(describer.Requests);
        Assert.Single(images);
        Assert.Null(options.Context);
    }

    [Fact]
    public async Task OcrFailsAlone_ItemIsIndexed_WithARetryableOcrFailure()
    {
        // A breaker that never opens, so the scheduler retries the transient failure and then gives up.
        var describer = new FakeImageDescriber();
        var ocrProvider = new FakeOcrProvider(FakeProviderBehavior.AlwaysFailing(AiFailureKind.Unreachable));
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), describer, ocrProvider],
            services => services.AddSingleton<IProviderCircuitBreaker>(new OpenBreaker(null)),
            SampleMedia.JpegWithExif);

        var jobId = await AnalyzeAsync(items);

        var item = items[0];
        var ocr = await StateAsync(item, AnalyzerKeys.Ocr);
        Assert.Equal(AnalyzerStatus.Failed, ocr.Status);
        Assert.True(ocr.IsRetryable);
        Assert.Equal(nameof(AiFailureKind.Unreachable), ocr.FailureKind);
        Assert.Equal(MediaItemStatus.Indexed, (await bed!.Library.Items.GetAsync(item.Id, Token))!.Status);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(item, AnalyzerKeys.Describer)).Status);

        // Five attempts; the retries ran only OCR.
        Assert.Equal(5, ocrProvider.Calls);
        Assert.Equal(1, describer.Calls);
        Assert.Equal(WorkItemState.Failed, Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).State);
    }

    [Fact]
    public async Task EmbeddingFails_OthersAreStored_AndTheItemFailsOnlyWhenTheSchedulerGivesUp()
    {
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(FakeProviderBehavior.AlwaysFailing(AiFailureKind.InvalidInput)), new FakeImageDescriber(), new FakeOcrProvider()],
            SampleMedia.JpegWithExif);

        var jobId = await AnalyzeAsync(items);

        var item = items[0];
        var embedding = await StateAsync(item, AnalyzerKeys.Embedding);
        Assert.Equal(AnalyzerStatus.Failed, embedding.Status);
        Assert.False(embedding.IsRetryable);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(item, AnalyzerKeys.Describer)).Status);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(item, AnalyzerKeys.Ocr)).Status);
        Assert.Equal(WorkItemState.Failed, Assert.Single(await bed!.Library.Jobs.GetWorkItemsAsync(jobId, Token)).State);

        // A permanent failure isn't retried: the scheduler calls OnFailedAsync at once.
        var failed = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.Equal(MediaItemStatus.Error, failed.Status);
        Assert.Equal(AnalysisStages.EmbeddingError, failed.Error?.Stage);
        Assert.True(failed.Error?.IsPermanent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetryFailedItems_AnalyzesAnItemWhoseEmbeddingFailed_InAnAnalysisJob(bool inTheSameJob)
    {
        // The embedding keeps failing transiently until the provider is back, then the item is Error (not permanent).
        var failing = true;
        var embedder = new FakeEmbeddingProvider(imageKey: _ => failing
            ? throw new AiProviderException(AiFailureKind.Unreachable, "fake:fake-embedder", "down")
            : "picture");
        var items = await ImportAsync(
            [embedder, new FakeImageDescriber(), new FakeOcrProvider()],
            services => services.AddSingleton<IProviderCircuitBreaker>(new OpenBreaker(null)),
            SampleMedia.JpegWithExif);
        var jobId = await AnalyzeAsync(items);
        var failed = (await bed!.Library.Items.GetAsync(items[0].Id, Token))!;
        Assert.Equal(MediaItemStatus.Error, failed.Status);
        Assert.False(failed.Error?.IsPermanent);

        failing = false;
        var result = await bed.Sender.Send(new RetryFailedItemsCommand(inTheSameJob ? jobId : null), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(1, result.Retried);
        Assert.Equal(MediaItemStatus.Indexed, (await bed.Library.Items.GetAsync(items[0].Id, Token))!.Status);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(items[0], AnalyzerKeys.Embedding)).Status);
        var work = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        Assert.Equal(inTheSameJob ? 2 : 1, work.Count);
        Assert.All(work, workItem => Assert.Equal(AnalysisStages.Item, workItem.Stage));
    }

    [Fact]
    public async Task ProviderPausedByItsBreaker_IsNotCalled_AndTheWorkItemIsParked()
    {
        var describer = new FakeImageDescriber();
        var breaker = new OpenBreaker(describer.Id);
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), describer, new FakeOcrProvider()],
            services => services.AddSingleton<IProviderCircuitBreaker>(breaker),
            SampleMedia.JpegWithExif);

        var jobId = await AnalyzeAsync(items);

        var item = items[0];
        Assert.Equal(0, describer.Calls);
        var state = await StateAsync(item, AnalyzerKeys.Describer);
        Assert.Equal(AnalyzerStatus.Skipped, state.Status);
        Assert.Equal(AnalyzerReasons.ProviderUnavailable, state.ReasonCode);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(item, AnalyzerKeys.Ocr)).Status);
        Assert.Equal(MediaItemStatus.Indexed, (await bed!.Library.Items.GetAsync(item.Id, Token))!.Status);
        Assert.Contains("unavailable", Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).LastError, StringComparison.Ordinal);
        Assert.Contains("fake:ocr", breaker.Successes);
    }

    [Fact]
    public async Task NoDescriberInTheProfile_PrefersAServerDescriber_OverAnInProcessOne()
    {
        var inProcess = new FakeImageDescriber(modelName: "in-process") { Kind = AiProviderKind.Onnx };
        var server = new FakeImageDescriber(modelName: "server");
        var items = await ImportAsync([new FakeEmbeddingProvider(), inProcess, server], SampleMedia.JpegWithExif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        Assert.Equal(0, inProcess.Calls);
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task NoServerDescriber_TheInProcessOneDescribes()
    {
        var inProcess = new FakeImageDescriber(modelName: "in-process") { Kind = AiProviderKind.Onnx };
        var items = await ImportAsync([new FakeEmbeddingProvider(), inProcess], SampleMedia.JpegWithExif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        Assert.Equal(1, inProcess.Calls);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(items[0], AnalyzerKeys.Describer)).Status);
    }

    [Fact]
    public async Task ServerDescriberPaused_ItemsWaitForIt_InsteadOfFallingBackToTheInProcessOne()
    {
        var inProcess = new FakeImageDescriber(modelName: "in-process") { Kind = AiProviderKind.Onnx };
        var server = new FakeImageDescriber(modelName: "server");
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), inProcess, server],
            services => services.AddSingleton<IProviderCircuitBreaker>(new OpenBreaker(server.Id)),
            SampleMedia.JpegWithExif);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        Assert.Equal(0, inProcess.Calls);
        Assert.Equal(AnalyzerReasons.ProviderUnavailable, (await StateAsync(items[0], AnalyzerKeys.Describer)).ReasonCode);
    }

    [Fact]
    public async Task InProcessDescriberNamedInTheProfile_IsUsed_ThoughAServerDescriberExists()
    {
        var inProcess = new FakeImageDescriber(modelName: "in-process") { Kind = AiProviderKind.Onnx };
        var server = new FakeImageDescriber(modelName: "server");
        var items = await ImportAsync([new FakeEmbeddingProvider(), inProcess, server], SampleMedia.JpegWithExif);
        await UseDescriberAsync(inProcess);

        await AnalyzeAsync(items, [AnalyzerKeys.Describer]);

        Assert.Equal(1, inProcess.Calls);
        Assert.Equal(0, server.Calls);
    }

    [Fact]
    public async Task NoDescriberOrOcrProvider_SkipsThem_AndTheItemIsIndexed()
    {
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), new FakeImageDescriber { IsSupportedOnCurrentPlatform = false }],
            SampleMedia.JpegWithExif);

        await AnalyzeAsync(items);

        var item = items[0];
        Assert.Equal(AnalyzerReasons.UnsupportedPlatform, (await StateAsync(item, AnalyzerKeys.Describer)).ReasonCode);
        Assert.Equal(AnalyzerReasons.NoProvider, (await StateAsync(item, AnalyzerKeys.Ocr)).ReasonCode);
        Assert.Equal(MediaItemStatus.Indexed, (await bed!.Library.Items.GetAsync(item.Id, Token))!.Status);
    }

    [Theory]
    [InlineData(true, "none", 0, "")]
    [InlineData(true, "some", 1, "SIGN")]
    [InlineData(true, "missing", 1, "SIGN")]
    [InlineData(false, "none", 1, "SIGN")]
    public async Task PreFilterWithDetector_SkipsTheProviderOnlyWhenTheDetectorFindsNoText(bool preFilter, string detected, int expectedCalls, string expectedText)
    {
        var ocr = new FakeOcrProvider(recognize: _ => "SIGN");
        var detector = new FakeDetectingOcrProvider(_ => detected switch
        {
            "some" => true,
            "none" => false,
            _ => throw new AiProviderException(AiFailureKind.ModelMissing, "fake:ocr-detector", "not installed"),
        });
        var items = await ImportAsync([new FakeEmbeddingProvider(), new FakeImageDescriber(), ocr, detector], SampleMedia.JpegWithExif);
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Ocr) { ProviderId = ocr.Id, PreFilterWithDetector = preFilter });
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(profile), Token);

        await AnalyzeAsync(items);

        Assert.Equal(expectedCalls, ocr.Calls);
        Assert.Equal(preFilter ? 1 : 0, detector.DetectorCalls);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(items[0], AnalyzerKeys.Ocr)).Status);
        var metadata = await bed.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal(expectedText.Length == 0 ? null : expectedText, metadata.GetEffectiveValue(DescriptiveField.OcrText)?.Value.Text);
    }

    [Fact]
    public async Task DisabledAnalyzer_IsSkipped()
    {
        var describer = new FakeImageDescriber();
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer, new FakeOcrProvider()], SampleMedia.JpegWithExif);
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { Enabled = false });
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(profile), Token);

        await AnalyzeAsync(items);

        Assert.Equal(AnalyzerReasons.Disabled, (await StateAsync(items[0], AnalyzerKeys.Describer)).ReasonCode);
        Assert.Equal(0, describer.Calls);
    }

    [Fact]
    public async Task DescriberModelMissing_IsSkippedAndRetryable()
    {
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), new FakeImageDescriber(FakeProviderBehavior.AlwaysFailing(AiFailureKind.ModelMissing)), new FakeOcrProvider()],
            SampleMedia.JpegWithExif);

        await AnalyzeAsync(items);

        var state = await StateAsync(items[0], AnalyzerKeys.Describer);
        Assert.Equal(AnalyzerReasons.ModelMissing, state.ReasonCode);
        Assert.True(state.IsRetryable);
        Assert.Equal(MediaItemStatus.Indexed, (await bed!.Library.Items.GetAsync(items[0].Id, Token))!.Status);
    }

    [Fact]
    public async Task RetryOfTheWorkItem_RunsOnlyWhatDidntFinish()
    {
        var attempts = 0;
        var describer = new FakeImageDescriber();
        var ocr = new FakeOcrProvider(recognize: _ => Interlocked.Increment(ref attempts) == 1
            ? throw new AiProviderException(AiFailureKind.Timeout, "fake:ocr", "slow")
            : "text");
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer, ocr], SampleMedia.JpegWithExif);

        var jobId = await AnalyzeAsync(items);

        Assert.Equal(1, describer.Calls);
        Assert.Equal(2, ocr.Calls);
        Assert.Equal(AnalyzerStatus.Done, (await StateAsync(items[0], AnalyzerKeys.Ocr)).Status);
        Assert.Equal(WorkItemState.Completed, Assert.Single(await bed!.Library.Jobs.GetWorkItemsAsync(jobId, Token)).State);
        Assert.Equal(MediaItemStatus.Indexed, (await bed.Library.Items.GetAsync(items[0].Id, Token))!.Status);
    }

    [Fact]
    public async Task OcrOnlyRun_LeavesTheItemsStatusAlone()
    {
        var ocr = new FakeOcrProvider(recognize: _ => "text");
        // An embedding that fails for good puts the item in Error; a retry of OCR alone doesn't change that.
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(FakeProviderBehavior.AlwaysFailing(AiFailureKind.InvalidInput)), new FakeImageDescriber(), ocr],
            SampleMedia.JpegWithExif);
        await AnalyzeAsync(items);
        Assert.Equal(MediaItemStatus.Error, (await bed!.Library.Items.GetAsync(items[0].Id, Token))!.Status);
        var describer = await StateAsync(items[0], AnalyzerKeys.Describer);

        await AnalyzeAsync(items, [AnalyzerKeys.Ocr]);

        Assert.Equal(MediaItemStatus.Error, (await bed.Library.Items.GetAsync(items[0].Id, Token))!.Status);
        Assert.Equal(2, ocr.Calls);
        Assert.Equal(describer, await StateAsync(items[0], AnalyzerKeys.Describer));
    }

    [Fact]
    public async Task FileGone_FailsTheItemOnlyWhenTheRunIncludesTheEmbedding()
    {
        var items = await ImportAsync([new FakeEmbeddingProvider(), new FakeImageDescriber(), new FakeOcrProvider()], SampleMedia.JpegWithExif);
        await AnalyzeAsync(items);
        File.Delete(Path.Combine(bed!.Root, Path.GetFileName(SampleMedia.JpegWithExif)));

        var ocrOnly = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(await AnalyzeAsync(items, [AnalyzerKeys.Ocr]), Token));
        Assert.Equal(WorkItemState.Failed, ocrOnly.State);
        Assert.Equal(MediaItemStatus.Indexed, (await bed.Library.Items.GetAsync(items[0].Id, Token))!.Status);

        Assert.Equal(WorkItemState.Failed, Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(await AnalyzeAsync(items), Token)).State);
        var failed = (await bed.Library.Items.GetAsync(items[0].Id, Token))!;
        Assert.Equal(MediaItemStatus.Error, failed.Status);
        Assert.Equal(AnalysisStages.Item, failed.Error?.Stage);
    }

    [Fact]
    public async Task Cancelled_KeepsTheFinishedResults_AndLeavesTheItemPending()
    {
        var describer = new FakeImageDescriber(new FakeProviderBehavior { Latency = TimeSpan.FromMinutes(5) });
        var items = await ImportAsync([new FakeEmbeddingProvider(), describer, new FakeOcrProvider()], SampleMedia.JpegWithExif);
        var jobId = await bed!.Sender.Send(new AnalyzeItemsCommand([items[0].Id]), Token);

        var stopwatch = Stopwatch.StartNew();
        while (describer.Calls == 0)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "The describer never started.");
            await Task.Delay(20, Token);
        }

        await bed.Jobs.Current!.CancelJobAsync(jobId!.Value, Token);
        await bed.WaitUntilIdleAsync();

        var states = await bed.Library.AnalyzerStates.GetForItemAsync(items[0].Id, Token);
        Assert.Equal(AnalyzerKeys.Embedding, Assert.Single(states).Analyzer);
        Assert.Single(await bed.Library.Embeddings.GetAsync(items[0].Id, Token));
        Assert.Equal(MediaItemStatus.Pending, (await bed.Library.Items.GetAsync(items[0].Id, Token))!.Status);
    }

    [Fact]
    public async Task AnimationOcr_NoisyWatermarkVariantsOnEveryFrame_AreStoredOnce()
    {
        string[] reads = ["LONGINES PARIS", "LONGlNES PARIS", "LONGINES PARlS", "LONGINES PARIS"];
        var calls = 0;
        var ocr = new FakeOcrProvider(recognize: _ => reads[Interlocked.Increment(ref calls) % reads.Length]);
        var items = await ImportAsync([new FakeEmbeddingProvider(), new FakeImageDescriber(), ocr], SampleMedia.AnimatedGif);

        await AnalyzeAsync(items, [AnalyzerKeys.Ocr]);

        Assert.True(ocr.Calls > 1, "The animation was read on one frame only.");
        var metadata = await bed!.Library.Metadata.GetAsync(items[0].Id, Token);
        Assert.Equal("LONGINES PARIS", metadata.GetEffectiveValue(DescriptiveField.OcrText)?.Value.Text);
    }

    [Fact]
    public async Task AnimationWithoutFrames_BecauseFfmpegIsMissing_WaitsAsPending()
    {
        var sampler = new NoFramesSampler(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable);
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), new FakeImageDescriber(), new FakeOcrProvider()],
            services => services.AddSingleton<IFrameSampler>(sampler),
            SampleMedia.AnimatedGif);

        var jobId = await AnalyzeAsync(items);

        var embedding = await StateAsync(items[0], AnalyzerKeys.Embedding);
        Assert.Equal(AnalyzerStatus.Failed, embedding.Status);
        Assert.Equal(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable, embedding.ReasonCode);
        Assert.True(embedding.IsRetryable);
        Assert.Equal(AnalyzerReasons.NoFrames, (await StateAsync(items[0], AnalyzerKeys.Describer)).ReasonCode);
        Assert.Equal(MediaItemStatus.Pending, (await bed!.Library.Items.GetAsync(items[0].Id, Token))!.Status);
        Assert.Equal(WorkItemState.Completed, Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).State);
    }

    [Fact]
    public async Task AnimationWithoutFrames_BecauseItsStreamIsDamaged_FailsTheWorkItem()
    {
        var sampler = new NoFramesSampler(FrameSamplingWarnings.FrameSamplingFailed);
        var items = await ImportAsync(
            [new FakeEmbeddingProvider(), new FakeImageDescriber(), new FakeOcrProvider()],
            services => services.AddSingleton<IFrameSampler>(sampler),
            SampleMedia.AnimatedGif);

        var jobId = await AnalyzeAsync(items);

        Assert.Equal(FrameSamplingWarnings.FrameSamplingFailed, (await StateAsync(items[0], AnalyzerKeys.Embedding)).ReasonCode);
        Assert.Equal(WorkItemState.Failed, Assert.Single(await bed!.Library.Jobs.GetWorkItemsAsync(jobId, Token)).State);
    }

    private static ImageDescription FullDescription(string title) =>
        new(title, $"{title} description.", $"{title} summary.", ["dog"], ["ball"], ["park"], ["running"], ["joy"]);

    private async Task UseDescriberAsync(FakeImageDescriber describer)
    {
        var profile = AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ProviderId = describer.Id });
        await bed!.Sender.Send(new UpdateAnalysisProfileCommand(profile), Token);
    }

    private Task<IReadOnlyList<MediaItem>> ImportAsync(IAiProvider[] providers, params string[] files) =>
        ImportAsync(providers, null, files);

    private async Task<IReadOnlyList<MediaItem>> ImportAsync(IAiProvider[] providers, Action<IServiceCollection>? configure, params string[] files)
    {
        bed = await ImportTestBed.CreateAsync(configure: services =>
        {
            services.AddSingleton<IAiProviderRegistry>(new AiProviderRegistry(providers, TimeProvider.System));
            configure?.Invoke(services);
        });
        foreach (var file in files)
        {
            bed.Copy(file, Path.GetFileName(file));
        }

        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var items = await bed.ItemsAsync();
        Assert.Equal(files.Length, items.Count);
        Assert.All(items, item => Assert.Equal(MediaItemStatus.Pending, item.Status));
        return items;
    }

    private async Task<Id<ProcessingJob>> AnalyzeAsync(IReadOnlyList<MediaItem> items, IReadOnlyCollection<string>? analyzers = null)
    {
        var jobId = await bed!.Sender.Send(new AnalyzeItemsCommand([.. items.Select(item => item.Id)], analyzers), Token);
        await bed.WaitUntilIdleAsync();
        return Assert.NotNull(jobId);
    }

    private async Task<AnalyzerState> StateAsync(MediaItem item, string analyzer) =>
        Assert.Single(await bed!.Library.AnalyzerStates.GetForItemAsync(item.Id, Token), state => state.Analyzer == analyzer);

    private sealed class OpenBreaker(string? openProviderId) : IProviderCircuitBreaker
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public List<string> Successes { get; } = [];

        public IReadOnlyList<ProviderCircuitStatus> OpenCircuits => [];

        public bool IsOpen(string providerId) => providerId == openProviderId;

        public void RecordSuccess(string providerId)
        {
            lock (Successes)
            {
                Successes.Add(providerId);
            }
        }

        public bool RecordFailure(ClassifiedError error) => false;

        public Task CheckNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoFramesSampler(string code) : IFrameSampler
    {
        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(FrameSample.Failed(new ExtractionWarning(code)));
    }
}
