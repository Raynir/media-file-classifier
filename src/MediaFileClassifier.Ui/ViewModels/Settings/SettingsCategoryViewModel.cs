using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A category in the settings window's sidebar, with its entries.
/// </summary>
public sealed class SettingsCategoryViewModel : ViewModelBase
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsCategoryViewModel"/> class.
    /// </summary>
    /// <param name="category">One of <c>SettingCategories</c>.</param>
    /// <param name="entries">The category's entries, in order.</param>
    /// <param name="localizer">UI strings.</param>
    public SettingsCategoryViewModel(string category, IReadOnlyList<SettingsEntryViewModel> entries, Localizer localizer)
    {
        this.localizer = localizer;
        Category = category;
        Entries = entries;
    }

    /// <summary>
    /// Gets the category, one of <c>SettingCategories</c>.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the category's name.
    /// </summary>
    public string Label => localizer.Get(LabelKeyOf(Category));

    /// <summary>
    /// Gets the entries, in order.
    /// </summary>
    public IReadOnlyList<SettingsEntryViewModel> Entries { get; }

    /// <summary>
    /// Gets the resource key of a category's name.
    /// </summary>
    /// <param name="category">One of <c>SettingCategories</c>.</param>
    /// <returns>The resource key, <c>SettingsCategory_&lt;category&gt;</c>.</returns>
    public static string LabelKeyOf(string category) => "SettingsCategory_" + category;

    /// <summary>
    /// Updates the texts after a culture or pseudo-localisation switch.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Label));
        foreach (var entry in Entries)
        {
            entry.RefreshTexts();
        }
    }
}
