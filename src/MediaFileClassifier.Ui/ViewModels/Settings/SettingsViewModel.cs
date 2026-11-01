using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Settings;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The settings window (E17-S01): a sidebar of categories and a control for every registered setting.
/// Changes are saved as they are made. One instance lives for the app's lifetime.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore store;

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

            OnPropertyChanged(nameof(ShowsLibraryNote));
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
    /// Gets or sets the category shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleEntries), nameof(Heading), nameof(IsEmpty), nameof(ShowsLibraryNote))]
    public partial SettingsCategoryViewModel? SelectedCategory { get; set; }

    /// <summary>
    /// Gets the heading above the entries.
    /// </summary>
    public string Heading => SelectedCategory?.Label ?? string.Empty;

    /// <summary>
    /// Gets the entries shown.
    /// </summary>
    public IReadOnlyList<SettingsEntryViewModel> VisibleEntries => SelectedCategory?.Entries ?? [];

    /// <summary>
    /// Gets a value indicating whether there are no entries to show.
    /// </summary>
    public bool IsEmpty => VisibleEntries.Count == 0;

    /// <summary>
    /// Gets a value indicating whether to explain that library settings need an open library.
    /// </summary>
    public bool ShowsLibraryNote => !store.IsAvailable(SettingScope.Library)
        && VisibleEntries.OfType<SettingItemViewModel>().Any(item => item.IsLibrarySetting);

    private IEnumerable<SettingItemViewModel> Items => Categories.SelectMany(category => category.Entries).OfType<SettingItemViewModel>();

    private static SettingItemViewModel CreateItem(SettingDefinition setting, SettingItemServices services) => setting switch
    {
        BooleanSetting boolean => new BooleanSettingItemViewModel(boolean, services),
        IntegerSetting integer => new IntegerSettingItemViewModel(integer, services),
        TextSetting text => new TextSettingItemViewModel(text, services),
        IChoiceSetting => new ChoiceSettingItemViewModel(setting, services),
        _ => throw new NotSupportedException($"Setting {setting.Key} has a type the settings window can't show: {setting.GetType().Name}."),
    };
}
