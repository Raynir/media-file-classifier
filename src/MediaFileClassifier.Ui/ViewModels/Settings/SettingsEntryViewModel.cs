using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// One entry in a settings category: a generated setting control or a custom section.
/// </summary>
public abstract class SettingsEntryViewModel : ViewModelBase
{
    private protected SettingsEntryViewModel(string category, string labelKey, Localizer localizer)
    {
        Category = category;
        LabelKey = labelKey;
        Localizer = localizer;
    }

    /// <summary>
    /// Gets the category, one of <c>SettingCategories</c>.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label => Localizer.Get(LabelKey);

    /// <summary>
    /// Gets the category's name, shown next to search results.
    /// </summary>
    public string CategoryLabel => Localizer.Get(SettingsCategoryViewModel.LabelKeyOf(Category));

    /// <summary>
    /// Gets or sets a value indicating whether the category's name is shown, as it is in search results.
    /// </summary>
    public bool ShowsCategory
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Gets the UI strings.
    /// </summary>
    private protected Localizer Localizer { get; }

    private string LabelKey { get; }

    /// <summary>
    /// Checks whether the entry matches a search.
    /// </summary>
    /// <param name="search">The search text, trimmed and not empty.</param>
    /// <returns><c>true</c> if the label contains the text, ignoring case.</returns>
    public bool Matches(string search) => Label.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Updates the texts after a culture or pseudo-localisation switch.
    /// </summary>
    public virtual void RefreshTexts() => OnPropertyChanged(string.Empty);
}
