using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Theming;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingsViewModel"/> and the generated controls (E17-S01-T02).
/// </summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly InMemorySettingsStore store = new();
    private readonly ServiceProvider services;

    public SettingsViewModelTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ISettingsStore>(store)
            .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new Commands.FakePlatformServices())
            .AddUseCases()
            .AddUi()
            .AddSingleton(new CustomSettingsSection(SettingCategories.AiModels, "Setting_Theme", () => new FakeSectionViewModel()) { Order = 50 })
            .BuildServiceProvider();
    }

    [Fact]
    public void EveryRegisteredSetting_HasASuitableControlInItsCategory()
    {
        var viewModel = CreateViewModel();
        var registry = services.GetRequiredService<SettingRegistry>();

        Assert.NotEmpty(registry.All);
        foreach (var setting in registry.All)
        {
            var category = viewModel.Categories.Single(category => category.Category == setting.Category);
            var item = Assert.Single(category.Entries.OfType<SettingItemViewModel>(), item => item.Definition == setting);
            Assert.IsType(
                setting switch
                {
                    BooleanSetting => typeof(BooleanSettingItemViewModel),
                    IntegerSetting => typeof(IntegerSettingItemViewModel),
                    TextSetting => typeof(TextSettingItemViewModel),
                    _ => typeof(ChoiceSettingItemViewModel),
                },
                item);
        }
    }

    [Fact]
    public void EveryRegisteredSetting_HasItsTextsInTheResources()
    {
        var entries = CreateViewModel().Categories.SelectMany(category => category.Entries).OfType<SettingItemViewModel>().ToList();

        var texts = entries.SelectMany(item => new[] { item.Label, item.Help, item.CategoryLabel, (item as TextSettingItemViewModel)?.Placeholder })
            .Concat(entries.OfType<ChoiceSettingItemViewModel>().SelectMany(item => item.Options.Select(option => option.Label)))
            .OfType<string>();

        Assert.DoesNotContain(texts, text => text.StartsWith('!'));
    }

    [Fact]
    public void Categories_AreTheMvpCategoriesInOrder()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(SettingCategories.All, viewModel.Categories.Select(category => category.Category));
        Assert.Same(viewModel.Categories[0], viewModel.SelectedCategory);
        Assert.Equal("General", viewModel.Heading);
        Assert.True(viewModel.Categories.Single(category => category.Category == SettingCategories.Processing).Entries.Count == 0);
    }

    [Fact]
    public void CustomSection_AppearsInItsCategoryByOrder()
    {
        var viewModel = CreateViewModel();

        var entries = viewModel.Categories.Single(category => category.Category == SettingCategories.AiModels).Entries;

        var section = Assert.IsType<CustomSettingsSectionViewModel>(entries[1]);
        Assert.IsType<FakeSectionViewModel>(section.Content);
        Assert.Same(KnownSettings.OpenAiCompatibleEndpoint, ((SettingItemViewModel)entries[0]).Definition);
    }

    [Fact]
    public void ChoiceControl_SavesTheSelectedOption()
    {
        var item = Item<ChoiceSettingItemViewModel>(ThemeService.ThemeSetting);

        item.Selected = item.Options.Single(option => Equals(option.Value, ThemeMode.Dark));

        Assert.Equal(ThemeMode.Dark, store.Get(ThemeService.ThemeSetting));
        Assert.Null(item.Error);
    }

    [Fact]
    public void Control_FollowsChangesMadeElsewhere()
    {
        var item = Item<ChoiceSettingItemViewModel>(ThemeService.ThemeSetting);

        store.SetValue(ThemeService.ThemeSetting, ThemeMode.Light);

        Assert.Equal(ThemeMode.Light, item.Selected?.Value);
    }

    [Fact]
    public void TextControl_InvalidText_ShowsAnErrorAndKeepsTheStoredValue()
    {
        var item = Item<TextSettingItemViewModel>(KnownSettings.OpenAiCompatibleEndpoint);

        item.Text = "localhost:1234";
        item.CommitCommand.Execute(null);

        Assert.Equal("Enter a URL that starts with http:// or https://, or leave it empty.", item.Error);
        Assert.Equal("localhost:1234", item.Text);
        Assert.Equal(string.Empty, store.Get(KnownSettings.OpenAiCompatibleEndpoint));
    }

    [Fact]
    public void TextControl_ValidText_IsSavedTrimmed()
    {
        var item = Item<TextSettingItemViewModel>(KnownSettings.OpenAiCompatibleEndpoint);
        item.Text = "localhost";
        item.CommitCommand.Execute(null);

        item.Text = " http://localhost:1234/v1 ";
        item.CommitCommand.Execute(null);

        Assert.Null(item.Error);
        Assert.Equal("http://localhost:1234/v1", store.Get(KnownSettings.OpenAiCompatibleEndpoint));
    }

    [Fact]
    public void LibrarySettings_WithoutALibrary_AreReadOnlyWithANote()
    {
        var viewModel = CreateViewModel();
        var item = Item<IntegerSettingItemViewModel>(KnownSettings.RetainedPreviousRuns, viewModel);

        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Category == SettingCategories.Library);

        Assert.False(item.IsEditable);
        Assert.Equal(1m, item.Value);
        Assert.True(item.IsLibrarySetting);
        Assert.Equal("Library", item.ScopeLabel);
        Assert.True(viewModel.ShowsLibraryNote);
    }

    [Fact]
    public void LibrarySettings_BecomeEditableWhenALibraryOpens()
    {
        var viewModel = CreateViewModel();
        var item = Item<IntegerSettingItemViewModel>(KnownSettings.RetainedPreviousRuns, viewModel);
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Category == SettingCategories.Library);

        store.IsLibraryOpen = true;
        item.Value = 3;

        Assert.True(item.IsEditable);
        Assert.False(viewModel.ShowsLibraryNote);
        Assert.Equal(3, store.Get(KnownSettings.RetainedPreviousRuns));
    }

    [Fact]
    public void IntegerControl_EmptyBox_ShowsAnError()
    {
        store.IsLibraryOpen = true;
        var item = Item<IntegerSettingItemViewModel>(KnownSettings.RetainedPreviousRuns);

        item.Value = null;

        Assert.Equal("Enter a whole number from 0 to 10.", item.Error);
        Assert.Equal(1, store.Get(KnownSettings.RetainedPreviousRuns));
    }

    [Fact]
    public void BooleanControl_SavesTheValue()
    {
        store.IsLibraryOpen = true;
        var item = Item<BooleanSettingItemViewModel>(KnownSettings.AnalyzeAutomatically);

        item.Value = false;

        Assert.False(store.Get(KnownSettings.AnalyzeAutomatically));
    }

    [Fact]
    public void ApplyModeNote_IsShownOnlyForDelayedSettings()
    {
        var restart = new BooleanSetting("test.restart", SettingScope.App, SettingCategories.General, "Setting_Theme", false)
        {
            ApplyMode = SettingApplyMode.RestartRequired,
        };
        var itemServices = new SettingItemServices(store, services.GetRequiredService<MediatR.ISender>(), Ui.Localization.Localizer.Current);

        Assert.Equal("Takes effect after a restart", new BooleanSettingItemViewModel(restart, itemServices).ApplyModeNote);
        Assert.Null(Item<TextSettingItemViewModel>(KnownSettings.OpenAiCompatibleEndpoint).ApplyModeNote);
    }

    public void Dispose() => services.Dispose();

    private SettingsViewModel CreateViewModel() => services.GetRequiredService<SettingsViewModel>();

    private T Item<T>(SettingDefinition setting, SettingsViewModel? viewModel = null)
        where T : SettingItemViewModel
        => (viewModel ?? CreateViewModel()).Categories
            .SelectMany(category => category.Entries)
            .OfType<T>()
            .Single(item => item.Definition == setting);

    private sealed class FakeSectionViewModel : ViewModelBase
    {
    }
}
