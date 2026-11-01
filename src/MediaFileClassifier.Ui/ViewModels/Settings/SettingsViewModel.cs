using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Settings;
using MediaFileClassifier.UseCases.Settings.ResetSettings;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The settings window (E17-S01): a sidebar of categories, a control for every registered setting, search by
/// label, and reset to defaults per category or for all settings. Changes are saved as they are made. One
/// instance lives for the app's lifetime.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore store;
    private readonly ISender sender;
    private readonly Localizer localizer;
    private SettingsCategoryViewModel? categoryBeforeSearch;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="registry">The registered settings.</param>
    /// <param name="store">The setting values.</param>
    /// <param name="sender">Sends the change commands.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="sections">Hand-built sections of categories.</param>
    public SettingsViewModel(
        SettingRegistry registry,
        ISettingsStore store,
        ISender sender,
        Localizer localizer,
        IEnumerable<CustomSettingsSection> sections)
    {
        this.store = store;
        this.sender = sender;
        this.localizer = localizer;
        var services = new SettingItemServices(store, sender, localizer);
        var sectionList = sections.ToList();
        Categories = [.. SettingCategories.All.Select(category => new SettingsCategoryViewModel(
            category,
            [.. registry.InCategory(category)
                .Select(setting => (setting.Order, Entry: (SettingsEntryViewModel)CreateItem(setting, services)))
                .Concat(sectionList
                    .Where(section => section.Category == category)
                    .Select(section => (section.Order, Entry: (SettingsEntryViewModel)new CustomSettingsSectionViewModel(section, localizer))))
                .OrderBy(pair => pair.Order)
                .Select(pair => pair.Entry)],
            localizer))];
        SelectedCategory = Categories[0];

        store.Changed += (_, e) =>
        {
            foreach (var item in Items.Where(item => item.Definition == e.Setting))
            {
                item.Refresh();
            }
        };
        store.AvailabilityChanged += (_, _) =>
        {
            foreach (var item in Items)
            {
                item.Refresh();
            }

            NotifyContentChanged();
        };
        localizer.Changed += (_, _) =>
        {
            foreach (var category in Categories)
            {
                category.RefreshTexts();
            }

            OnPropertyChanged(string.Empty);
        };
    }

    /// <summary>
    /// Gets the categories, in sidebar order.
    /// </summary>
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }

    /// <summary>
    /// Gets or sets the category shown. It's <c>null</c> while search results are shown; choosing a category ends the search.
    /// </summary>
    [ObservableProperty]
    public partial SettingsCategoryViewModel? SelectedCategory { get; set; }

    /// <summary>
    /// Gets or sets the search text. Settings whose label contains it are shown from every category.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether search results are shown.
    /// </summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>
    /// Gets the heading above the entries.
    /// </summary>
    public string Heading => IsSearching
        ? localizer.Format("Settings_SearchResults", SearchText.Trim())
        : SelectedCategory?.Label ?? string.Empty;

    /// <summary>
    /// Gets the entries shown: the selected category's, or the search results.
    /// </summary>
    public IReadOnlyList<SettingsEntryViewModel> VisibleEntries => IsSearching
        ? [.. Categories.SelectMany(category => category.Entries).Where(entry => entry.Matches(SearchText.Trim()))]
        : SelectedCategory?.Entries ?? [];

    /// <summary>
    /// Gets a value indicating whether there are no entries to show.
    /// </summary>
    public bool IsEmpty => VisibleEntries.Count == 0;

    /// <summary>
    /// Gets the text shown when there are no entries.
    /// </summary>
    public string EmptyMessage => IsSearching
        ? localizer.Format("Settings_NoResults", SearchText.Trim())
        : localizer.Get("Settings_EmptyCategory");

    /// <summary>
    /// Gets a value indicating whether to explain that library settings need an open library.
    /// </summary>
    public bool ShowsLibraryNote => !store.IsAvailable(SettingScope.Library)
        && VisibleEntries.OfType<SettingItemViewModel>().Any(item => item.IsLibrarySetting);

    /// <summary>
    /// Gets a value indicating whether the selected category has settings that can be reset now.
    /// </summary>
    public bool CanResetCategory => !IsSearching
        && SelectedCategory is not null
        && SelectedCategory.Entries.OfType<SettingItemViewModel>().Any(item => store.IsAvailable(item.Definition.Scope));

    /// <summary>
    /// Gets the question that confirms resetting the selected category.
    /// </summary>
    public string ResetCategoryQuestion => localizer.Format("Settings_ResetCategory_Confirm", SelectedCategory?.Label ?? string.Empty);

    /// <summary>
    /// Gets the question that confirms resetting all settings, with a note when library settings are left alone.
    /// </summary>
    public string ResetAllQuestion => store.IsAvailable(SettingScope.Library)
        ? localizer.Get("Settings_ResetAll_Confirm")
        : localizer.Get("Settings_ResetAll_Confirm") + " " + localizer.Get("Settings_ResetAll_NoLibrary");

    private IEnumerable<SettingItemViewModel> Items => Categories.SelectMany(category => category.Entries).OfType<SettingItemViewModel>();

    private static SettingItemViewModel CreateItem(SettingDefinition setting, SettingItemServices services) => setting switch
    {
        BooleanSetting boolean => new BooleanSettingItemViewModel(boolean, services),
        IntegerSetting integer => new IntegerSettingItemViewModel(integer, services),
        TextSetting text => new TextSettingItemViewModel(text, services),
        IChoiceSetting => new ChoiceSettingItemViewModel(setting, services),
        _ => throw new NotSupportedException($"Setting {setting.Key} has a type the settings window can't show: {setting.GetType().Name}."),
    };

    /// <summary>
    /// Resets the selected category's settings to their defaults.
    /// </summary>
    /// <returns>A task that completes when the settings are reset.</returns>
    [RelayCommand]
    private async Task ResetCategoryAsync()
    {
        if (SelectedCategory is { } category)
        {
            await sender.Send(new ResetSettingsCommand(category.Category));
            RefreshItems(category.Entries.OfType<SettingItemViewModel>());
        }
    }

    /// <summary>
    /// Resets every setting to its default. Library settings are reset only while a library is open.
    /// </summary>
    /// <returns>A task that completes when the settings are reset.</returns>
    [RelayCommand]
    private async Task ResetAllAsync()
    {
        await sender.Send(new ResetSettingsCommand(Category: null));
        RefreshItems(Items);
    }

    // Also clears errors and rejected text that the reset didn't change in the store.
    private static void RefreshItems(IEnumerable<SettingItemViewModel> items)
    {
        foreach (var item in items)
        {
            item.Refresh();
        }
    }

    partial void OnSelectedCategoryChanged(SettingsCategoryViewModel? value)
    {
        if (value is not null && IsSearching)
        {
            SearchText = string.Empty;
        }

        NotifyContentChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        foreach (var entry in Categories.SelectMany(category => category.Entries))
        {
            entry.ShowsCategory = IsSearching;
        }

        if (IsSearching && SelectedCategory is not null)
        {
            categoryBeforeSearch = SelectedCategory;
            SelectedCategory = null;
        }
        else if (!IsSearching && SelectedCategory is null)
        {
            SelectedCategory = categoryBeforeSearch ?? Categories[0];
        }

        NotifyContentChanged();
    }

    private void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(VisibleEntries));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(ShowsLibraryNote));
        OnPropertyChanged(nameof(CanResetCategory));
        OnPropertyChanged(nameof(ResetCategoryQuestion));
        OnPropertyChanged(nameof(ResetAllQuestion));
    }
}
