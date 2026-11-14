using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.ViewModels.Analysis;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
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

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private AnalysisProfileViewModel Create() => new(sender, store, TestLocalizer.EnUs());

    /// <summary>
    /// Answers the profile use cases for a library.
    /// </summary>
    private sealed class ProfileSender : FakeSender
    {
        public AnalysisProfile? Profile { get; set; } = AnalysisProfile.Default;

        public IReadOnlyList<ProviderChoice> Choices { get; set; } = [Siglip, Gemma, GemmaOcr];

        public Exception? SaveFailure { get; set; }

        public List<AnalysisProfile> Saved { get; } = [];

        protected override object? Handle(object request)
        {
            switch (request)
            {
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
