using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// Tests for searching and resetting settings (E17-S01-T03).
/// </summary>
public sealed class SettingsSearchAndResetTests : IDisposable
{
    private readonly InMemorySettingsStore store = new();
    private readonly ServiceProvider services;
    private readonly SettingsViewModel viewModel;

    public SettingsSearchAndResetTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ISettingsStore>(store)
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new Commands.FakePlatformServices())
            .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
            .AddUseCases()
            .AddUi()
            .BuildServiceProvider();
        viewModel = services.GetRequiredService<SettingsViewModel>();
    }

    [Fact]
    public void Search_FindsSettingsByLabelAcrossCategories()
    {
        viewModel.SearchText = "  ANALYSE ";

        var result = Assert.Single(viewModel.VisibleEntries);
        Assert.Equal("Analyse new items automatically", result.Label);
        Assert.True(result.ShowsCategory);
        Assert.Equal("Library", result.CategoryLabel);
        Assert.Null(viewModel.SelectedCategory);
        Assert.Equal("Results for “ANALYSE”", viewModel.Heading);
        Assert.True(viewModel.ShowsLibraryNote);
        Assert.False(viewModel.CanResetCategory);
    }

    [Fact]
    public void Search_WithoutMatches_SaysSo()
    {
        viewModel.SearchText = "zebra";

        Assert.True(viewModel.IsEmpty);
        Assert.Equal("No settings match “zebra”.", viewModel.EmptyMessage);
    }

    [Fact]
    public void ClearingTheSearch_ReturnsToTheCategory()
    {
        var appearance = viewModel.Categories.Single(category => category.Category == SettingCategories.Appearance);
        viewModel.SelectedCategory = appearance;

        viewModel.SearchText = "theme";
        viewModel.SearchText = string.Empty;

        Assert.Same(appearance, viewModel.SelectedCategory);
        Assert.False(viewModel.VisibleEntries.Single().ShowsCategory);
    }

    [Fact]
    public void ChoosingACategory_EndsTheSearch()
    {
        var library = viewModel.Categories.Single(category => category.Category == SettingCategories.Library);
        viewModel.SearchText = "theme";

        viewModel.SelectedCategory = library;

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Same(library.Entries, viewModel.VisibleEntries);
    }

    [Fact]
    public async Task ResetCategory_RestoresTheCategoryDefaultsOnly()
    {
        store.SetValue(ThemeService.ThemeSetting, ThemeMode.Dark);
        store.SetValue(KnownSettings.OpenAiCompatibleEndpoint, "http://localhost:1234");
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Category == SettingCategories.Appearance);

        Assert.True(viewModel.CanResetCategory);
        Assert.Equal("Reset all Appearance settings to their defaults?", viewModel.ResetCategoryQuestion);
        await viewModel.ResetCategoryCommand.ExecuteAsync(null);

        Assert.Equal(ThemeMode.System, store.Get(ThemeService.ThemeSetting));
        Assert.Equal("http://localhost:1234", store.Get(KnownSettings.OpenAiCompatibleEndpoint));
        var theme = viewModel.VisibleEntries.OfType<ChoiceSettingItemViewModel>().Single();
        Assert.Equal(ThemeMode.System, theme.Selected?.Value);
    }

    [Fact]
    public void ResetCategory_IsUnavailableForLibrarySettingsWithoutALibrary()
    {
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Category == SettingCategories.Library);

        Assert.False(viewModel.CanResetCategory);

        store.IsLibraryOpen = true;
        Assert.True(viewModel.CanResetCategory);
    }

    [Fact]
    public async Task ResetAll_RestoresEveryDefaultAndClearsErrors()
    {
        store.IsLibraryOpen = true;
        store.SetValue(ThemeService.ThemeSetting, ThemeMode.Light);
        store.SetValue(KnownSettings.RetainedPreviousRuns, 4);
        var endpoint = viewModel.Categories.SelectMany(category => category.Entries).OfType<TextSettingItemViewModel>().Single();
        endpoint.Text = "not a url";
        endpoint.CommitCommand.Execute(null);

        await viewModel.ResetAllCommand.ExecuteAsync(null);

        Assert.Equal(ThemeMode.System, store.Get(ThemeService.ThemeSetting));
        Assert.Equal(1, store.Get(KnownSettings.RetainedPreviousRuns));
        Assert.Null(endpoint.Error);
        Assert.Equal(string.Empty, endpoint.Text);
    }

    [Fact]
    public void ResetAllQuestion_MentionsLibrarySettingsOnlyWithoutALibrary()
    {
        Assert.Equal(
            "Reset every setting to its default? Library settings stay as they are, because no library is open.",
            viewModel.ResetAllQuestion);

        store.IsLibraryOpen = true;
        Assert.Equal("Reset every setting to its default?", viewModel.ResetAllQuestion);
    }

    public void Dispose() => services.Dispose();
}
