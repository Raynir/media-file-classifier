using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.ViewModels.Analysis;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases.Ai.Endpoints;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Analysis;

/// <summary>
/// End-to-end tests of the analysis profile and endpoint settings (E17-S02), on the real container with a real
/// library in a temporary folder.
/// </summary>
public sealed class AnalysisSettingsFlowTests : IAsyncDisposable
{
    private readonly TestServices app = TestServices.Build(fullStack: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public void AiAndModels_ListsTheProfileAndEndpointSections_AfterHardware()
    {
        var settings = app.Services.GetRequiredService<SettingsViewModel>();

        var aiModels = settings.Categories.Single(category => category.Category == SettingCategories.AiModels);

        var contents = aiModels.Entries.OfType<CustomSettingsSectionViewModel>().Select(section => section.Content).ToList();
        var order = contents.Select(content => content.GetType()).ToList();
        Assert.Contains(typeof(AnalysisProfileViewModel), order);
        Assert.Contains(typeof(EndpointsViewModel), order);
        Assert.True(order.IndexOf(typeof(AnalysisProfileViewModel)) < order.IndexOf(typeof(EndpointsViewModel)));
    }

    [Fact]
    public async Task AnEndpointSaved_BecomesAProviderTheProfileCanUse_AndTheChoiceIsStored()
    {
        var sender = app.Services.GetRequiredService<ISender>();
        var library = await app.Services.GetRequiredService<ILibraryStore>().CreateAsync(app.Root, "Family", Token);
        await app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(library);

        var endpoint = await sender.Send(
            new SaveAiEndpointCommand(new AiEndpointSettings(string.Empty, "LM Studio", IAiEndpointService.LmStudioBaseUrl, "google/gemma-4-e2b"), null),
            Token);
        var describerId = $"openai-compatible:{endpoint.Id}:describer";
        var editor = (await sender.Send(new GetAnalysisProfileQuery(), Token))!;

        Assert.Contains(editor.Analyzers.Single(choices => choices.Analyzer == AnalyzerKeys.Describer).Providers, provider => provider.Id == describerId);
        Assert.Contains(editor.Analyzers.Single(choices => choices.Analyzer == AnalyzerKeys.Ocr).Providers, provider => provider.Id == $"openai-compatible:{endpoint.Id}:ocr");
        Assert.NotEmpty(editor.Analyzers.Single(choices => choices.Analyzer == AnalyzerKeys.Embedding).Providers);

        await sender.Send(
            new UpdateAnalysisProfileCommand(editor.Profile.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ProviderId = describerId, ModelId = "google/gemma-4-e2b", MaxTags = 5 })),
            Token);

        var stored = (await sender.Send(new GetAnalysisProfileQuery(), Token))!.Profile.Find(AnalyzerKeys.Describer)!;
        Assert.Equal(describerId, stored.ProviderId);
        Assert.Equal(5, stored.MaxTags);
    }

    [Fact]
    public async Task GetProfile_WithoutALibrary_IsNull()
    {
        var editor = await app.Services.GetRequiredService<ISender>().Send(new GetAnalysisProfileQuery(), Token);

        Assert.Null(editor);
    }

    [Fact]
    public async Task ARemovedEndpoint_LeavesTheProfileChoice_ShownAsUnavailable()
    {
        var sender = app.Services.GetRequiredService<ISender>();
        var library = await app.Services.GetRequiredService<ILibraryStore>().CreateAsync(app.Root, "Family", Token);
        await app.Services.GetRequiredService<ILibrarySession>().SetCurrentAsync(library);
        var endpoint = await sender.Send(new SaveAiEndpointCommand(new AiEndpointSettings(string.Empty, "LM Studio", IAiEndpointService.LmStudioBaseUrl, "m"), null), Token);
        var describerId = $"openai-compatible:{endpoint.Id}:describer";
        await sender.Send(new UpdateAnalysisProfileCommand(AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Describer) { ProviderId = describerId })), Token);
        await sender.Send(new RemoveAiEndpointCommand(endpoint.Id), Token);
        var page = app.Services.GetRequiredService<AnalysisProfileViewModel>();

        await page.RefreshAsync();

        var describer = page.Analyzers.Single(card => card.Analyzer == AnalyzerKeys.Describer);
        Assert.Equal(describerId, describer.SelectedProvider!.Value);
        Assert.True(describer.IsWarning);
    }
}
