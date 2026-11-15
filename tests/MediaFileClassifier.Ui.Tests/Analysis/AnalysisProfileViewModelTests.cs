using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.ViewModels.Analysis;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;

namespace MediaFileClassifier.Ui.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalysisProfileViewModel"/> (E17-S02-T01): every analyzer can be configured, changes are saved
/// as they are made, and an analyzer that can't run says why.
/// </summary>
public sealed class AnalysisProfileViewModelTests
{
    private static readonly ProviderChoice Siglip = new("onnx:siglip", "SigLIP 2", "siglip2", ProviderHealth.Healthy, true);
    private static readonly ProviderChoice Gemma = new("openai-compatible:e1:describer", "LM Studio (gemma)", "gemma", ProviderHealth.Healthy, true);
    private static readonly ProviderChoice Llava = new("openai-compatible:e2:describer", "Ollama (llava)", "llava", ProviderHealth.Healthy, true);
    private static readonly ProviderChoice GemmaOcr = new("openai-compatible:e1:ocr", "LM Studio (gemma)", "gemma", ProviderHealth.Healthy, true);

    private readonly InMemorySettingsStore store = new();
    private readonly ProfileSender sender = new();

    [Fact]
    public async Task Refresh_ShowsEveryAnalyzer_WithItsStoredSettingsAndProviders()
    {
        sender.Profile = new AnalysisProfile(
        [
            new AnalyzerSettings(AnalyzerKeys.Embedding),
            new AnalyzerSettings(AnalyzerKeys.Describer) { MaxTags = 5, Language = "ru", ProviderId = Gemma.Id, ModelId = "gemma" },
            new AnalyzerSettings(AnalyzerKeys.Ocr) { Enabled = false },
        ]);
        sender.Choices = [Siglip, Gemma, Llava, GemmaOcr];
        var page = Create();

        await page.RefreshAsync();

        Assert.True(page.HasLibrary);
        Assert.Equal([AnalyzerKeys.Embedding, AnalyzerKeys.Describer, AnalyzerKeys.Ocr], page.Analyzers.Select(card => card.Analyzer));
        var describer = page.Analyzers[1];
        Assert.True(describer.HasDescriberOptions);
        Assert.Equal(["Automatic", "LM Studio (gemma)", "Ollama (llava)"], describer.ProviderOptions.Select(option => option.Label));
        Assert.Equal(Gemma.Id, describer.SelectedProvider!.Value);
        Assert.Equal(["Any model", "gemma"], describer.ModelOptions.Select(option => option.Label));
        Assert.Equal("gemma", describer.SelectedModel!.Value);
        Assert.Equal(5m, describer.MaxTags);
        Assert.Equal("ru", describer.SelectedLanguage!.Value);
        Assert.False(page.Analyzers[2].Enabled);
        Assert.False(page.Analyzers[0].HasDescriberOptions);
    }

    [Fact]
    public async Task Refresh_ShowsNothingToEdit_WhenNoLibraryIsOpen()
    {
        sender.Profile = null;
        var page = Create();

        await page.RefreshAsync();

        Assert.False(page.HasLibrary);
        Assert.True(page.HasNoLibrary);
        Assert.Empty(sender.Saved);
    }

    [Fact]
    public async Task Refresh_ShowsStoredSettings_WithoutSavingThem()
    {
        var page = Create();

        await page.RefreshAsync();

        Assert.Empty(sender.Saved);
    }

    [Fact]
    public async Task SwitchingAnAnalyzerOff_SavesTheProfile_AndNotesThatItIsSkipped()
    {
        var page = Create();
        await page.RefreshAsync();

        page.Analyzers[2].Enabled = false;

        var saved = Assert.Single(sender.Saved);
        Assert.False(saved.Find(AnalyzerKeys.Ocr)!.Enabled);
        Assert.True(saved.Find(AnalyzerKeys.Embedding)!.Enabled);
        Assert.Equal(AnalyzerNoteKind.Info, page.Analyzers[2].NoteKind);
        Assert.Contains("Turned off", page.Analyzers[2].Note);
    }

    [Fact]
    public async Task ChoosingAProvider_SavesIt_AndNarrowsTheModelsToThatProvider()
    {
        sender.Choices = [Siglip, Gemma, Llava, GemmaOcr];
        var page = Create();
        await page.RefreshAsync();
        var describer = page.Analyzers[1];

        describer.SelectedProvider = describer.ProviderOptions.Single(option => option.Value == Llava.Id);

        Assert.Equal(Llava.Id, sender.Saved.Last().Find(AnalyzerKeys.Describer)!.ProviderId);
        Assert.Equal(["Any model", "llava"], describer.ModelOptions.Select(option => option.Label));
    }

    [Fact]
    public async Task ChoosingADifferentProvider_ClearsAModelItDoesNotRun()
    {
        sender.Profile = new AnalysisProfile([new AnalyzerSettings(AnalyzerKeys.Describer) { ProviderId = Gemma.Id, ModelId = "gemma" }]);
        sender.Choices = [Gemma, Llava];
        var page = Create();
        await page.RefreshAsync();
        var describer = page.Analyzers[1];

        describer.SelectedProvider = describer.ProviderOptions.Single(option => option.Value == Llava.Id);

        var saved = sender.Saved.Last().Find(AnalyzerKeys.Describer)!;
        Assert.Equal(Llava.Id, saved.ProviderId);
        Assert.Null(saved.ModelId);
    }

    [Fact]
    public async Task ChangingTheDescriberOptions_SavesThemAndKeepsWhatTheEditorDoesNotEdit()
    {
        sender.Profile = new AnalysisProfile([new AnalyzerSettings(AnalyzerKeys.Describer) { MaxTags = 20, PromptVersion = "v3" }]);
        var page = Create();
        await page.RefreshAsync();
        var describer = page.Analyzers[1];

        describer.MaxTags = 8;
        describer.SelectedLanguage = describer.Languages.Single(option => option.Value == "en");
        describer.MaxTags = null;

        var saved = sender.Saved.Last().Find(AnalyzerKeys.Describer)!;
        Assert.Null(saved.MaxTags);
        Assert.Equal("en", saved.Language);
        Assert.Equal("v3", saved.PromptVersion);
        Assert.Equal(8, sender.Saved[0].Find(AnalyzerKeys.Describer)!.MaxTags);
    }

    [Fact]
    public async Task ChangingTheOcrPreFilter_SavesItOnTheOcrAnalyzerOnly()
    {
        sender.Profile = AnalysisProfile.Default;
        var page = Create();
        await page.RefreshAsync();
        var ocr = page.Analyzers.Single(card => card.Analyzer == AnalyzerKeys.Ocr);

        ocr.PreFilterWithDetector = true;

        Assert.True(ocr.HasOcrOptions);
        Assert.False(page.Analyzers.Single(card => card.Analyzer == AnalyzerKeys.Describer).HasOcrOptions);
        var saved = sender.Saved.Last();
        Assert.True(saved.Find(AnalyzerKeys.Ocr)!.PreFilterWithDetector);
        Assert.False(saved.Find(AnalyzerKeys.Describer)!.PreFilterWithDetector);
    }

    [Fact]
    public async Task QuickChanges_AreSavedOneAfterAnother_NoneIsLost()
    {
        var page = Create();
        await page.RefreshAsync();

        page.Analyzers[2].Enabled = false;
        page.Analyzers[0].Enabled = false;
        await WaitAsync(() => sender.Saved.Count == 2);

        var last = sender.Saved.Last();
        Assert.False(last.Find(AnalyzerKeys.Ocr)!.Enabled);
        Assert.False(last.Find(AnalyzerKeys.Embedding)!.Enabled);
    }

    [Fact]
    public async Task AnAnalyzerWithoutAProvider_IsMarkedUnavailable_WithWhatToDo()
    {
        sender.Choices = [Siglip];
        var page = Create();

        await page.RefreshAsync();

        var describer = page.Analyzers[1];
        Assert.True(describer.IsWarning);
        Assert.Contains("add an endpoint", describer.Note);
        Assert.False(page.Analyzers[0].HasNote);
    }

    [Fact]
    public async Task AProviderThatIsDown_ShowsItsMessage()
    {
        var down = Gemma with { Health = new ProviderHealth(ProviderHealthStatus.Unreachable, "http://127.0.0.1:1234/v1 isn't answering. Check that the server is running.") };
        sender.Choices = [Siglip, down, GemmaOcr];
        var page = Create();

        await page.RefreshAsync();

        Assert.True(page.Analyzers[1].IsWarning);
        Assert.Equal("http://127.0.0.1:1234/v1 isn't answering. Check that the server is running.", page.Analyzers[1].Note);
    }

    [Fact]
    public async Task AChosenProviderThatNoLongerExists_StaysListedAsUnavailable_AndWarns()
    {
        sender.Profile = new AnalysisProfile([new AnalyzerSettings(AnalyzerKeys.Describer) { ProviderId = "openai-compatible:gone:describer" }]);
        sender.Choices = [Gemma];
        var page = Create();

        await page.RefreshAsync();

        var describer = page.Analyzers[1];
        Assert.Equal("openai-compatible:gone:describer", describer.SelectedProvider!.Value);
        Assert.Contains("(not available)", describer.SelectedProvider.Label);
        Assert.True(describer.IsWarning);
        Assert.Empty(sender.Saved);
    }

    [Fact]
    public async Task ASaveThatFails_ShowsAnError_AndTheStoredSettingsAgain()
    {
        var page = Create();
        await page.RefreshAsync();
        sender.SaveFailure = new IOException("disk full");

        page.Analyzers[0].Enabled = false;

        await WaitAsync(() => page.HasError);
        Assert.True(page.HasError);
        Assert.True(page.Analyzers[0].Enabled);
    }

    [Fact]
    public async Task OpeningALibrary_ShowsItsProfile()
    {
        sender.Profile = null;
        var page = Create();
        await page.RefreshAsync();
        sender.Profile = AnalysisProfile.Default;

        await page.RefreshAsync();

        Assert.True(page.HasLibrary);
    }

    [Theory]
    [InlineData(1, "1 item is outdated by this change.")]
    [InlineData(12, "12 items are outdated by this change.")]
    public async Task AChangeThatMakesItemsOutdated_ShowsTheReindexPrompt_WithTheCount(int count, string text)
    {
        var page = Create();
        await page.RefreshAsync();
        sender.Outdated = count;

        page.Analyzers[1].SelectedModel = page.Analyzers[1].ModelOptions.Single(option => option.Value == "gemma");

        await WaitAsync(() => page.HasOutdated);
        Assert.Equal(count, page.OutdatedCount);
        Assert.Equal(text, page.OutdatedText);
        Assert.Equal(1, sender.OutdatedQueries);
    }

    [Fact]
    public async Task ReindexNow_QueuesTheOutdatedItems_AndHidesThePrompt()
    {
        var page = await WithPromptAsync();

        await page.ReindexOutdatedCommand.ExecuteAsync(null);

        var queued = Assert.Single(sender.Queued);
        Assert.Same(AnalysisScope.Outdated, queued.Scope);
        Assert.Null(queued.Analyzers);
        Assert.False(page.HasOutdated);
        Assert.False(page.HasError);
    }

    [Fact]
    public async Task ReindexNow_WhenNothingIsQueued_KeepsThePrompt_AndShowsAnError()
    {
        var page = await WithPromptAsync();
        sender.QueueNothing = true;

        await page.ReindexOutdatedCommand.ExecuteAsync(null);

        Assert.True(page.HasOutdated);
        Assert.True(page.HasError);
    }

    [Fact]
    public async Task Later_HidesThePrompt_WithoutQueuingAnything()
    {
        var page = await WithPromptAsync();

        page.ReindexLaterCommand.Execute(null);

        Assert.False(page.HasOutdated);
        Assert.Empty(sender.Queued);
    }

    [Fact]
    public async Task AChangeThatMakesNothingOutdated_ShowsNoPrompt()
    {
        var page = Create();
        await page.RefreshAsync();
        sender.Outdated = 0;

        page.Analyzers[2].Enabled = false;

        await WaitAsync(() => sender.OutdatedQueries == 1);
        Assert.Equal(1, sender.OutdatedQueries);
        Assert.False(page.HasOutdated);
    }

    [Fact]
    public async Task ACountThatFails_HidesThePrompt_AndKeepsTheChange()
    {
        var page = await WithPromptAsync();
        sender.CountFailure = new IOException("locked");

        page.Analyzers[2].Enabled = false;

        await WaitAsync(() => sender.OutdatedQueries == 2);
        await WaitAsync(() => !page.HasOutdated);
        Assert.False(page.HasOutdated);
        Assert.False(page.HasError);
        Assert.False(sender.Saved.Last().Find(AnalyzerKeys.Ocr)!.Enabled);
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private AnalysisProfileViewModel Create() => new(sender, store, TestLocalizer.EnUs());

    private async Task<AnalysisProfileViewModel> WithPromptAsync()
    {
        var page = Create();
        await page.RefreshAsync();
        sender.Outdated = 3;
        page.Analyzers[0].Enabled = false;
        await WaitAsync(() => page.HasOutdated);
        Assert.True(page.HasOutdated);
        return page;
    }

    /// <summary>
    /// Answers the profile use cases for a library.
    /// </summary>
    private sealed class ProfileSender : FakeSender
    {
        public AnalysisProfile? Profile { get; set; } = AnalysisProfile.Default;

        public IReadOnlyList<ProviderChoice> Choices { get; set; } = [Siglip, Gemma, GemmaOcr];

        public Exception? SaveFailure { get; set; }

        public List<AnalysisProfile> Saved { get; } = [];

        public int Outdated { get; set; }

        public int OutdatedQueries { get; private set; }

        public Exception? CountFailure { get; set; }

        public bool QueueNothing { get; set; }

        public List<AnalyzeScopeCommand> Queued { get; } = [];

        protected override object? Handle(object request)
        {
            switch (request)
            {
                case GetOutdatedAnalysisQuery:
                    OutdatedQueries++;
                    if (CountFailure is { } countFailure)
                    {
                        throw countFailure;
                    }

                    return new OutdatedAnalysis(new Dictionary<string, IReadOnlyList<Id<MediaItem>>>
                    {
                        [AnalyzerKeys.Describer] = [.. Enumerable.Range(0, Outdated).Select(_ => Id<MediaItem>.New())],
                    });
                case AnalyzeScopeCommand command:
                    Queued.Add(command);
                    return new AnalyzeScopeResult(QueueNothing ? null : Id<ProcessingJob>.New(), Outdated);
                case GetAnalysisProfileQuery:
                    return Profile is null
                        ? null
                        : new AnalysisProfileEditorModel(
                            Profile,
                            [
                                new AnalyzerChoices(AnalyzerKeys.Embedding, [.. Choices.Where(choice => choice.Id.StartsWith("onnx", StringComparison.Ordinal))]),
                                new AnalyzerChoices(AnalyzerKeys.Describer, [.. Choices.Where(choice => choice.Id.EndsWith("describer", StringComparison.Ordinal))]),
                                new AnalyzerChoices(AnalyzerKeys.Ocr, [.. Choices.Where(choice => choice.Id.EndsWith("ocr", StringComparison.Ordinal))]),
                            ]);
                case UpdateAnalysisProfileCommand update:
                    if (SaveFailure is { } failure)
                    {
                        SaveFailure = null;
                        throw failure;
                    }

                    Saved.Add(update.Profile);
                    Profile = update.Profile;
                    return null;
                default:
                    return base.Handle(request);
            }
        }
    }
}
