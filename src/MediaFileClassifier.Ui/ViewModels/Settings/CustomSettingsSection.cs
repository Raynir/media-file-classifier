using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A hand-built part of a settings category, for settings that a generated control can't show well, such as
/// model management under AI &amp; Models (E17-S01-T02). Register one as a singleton; its view model is shown
/// with the view that <see cref="ViewLocator"/> maps to it.
/// </summary>
/// <param name="Category">One of <c>SettingCategories</c>.</param>
/// <param name="LabelKey">Resource key of the section's heading, which search matches too.</param>
/// <param name="CreateViewModel">Creates the section's view model when the settings window first needs it.</param>
public sealed record CustomSettingsSection(string Category, string LabelKey, Func<ViewModelBase> CreateViewModel)
{
    /// <summary>
    /// Gets the position among the category's entries; lower comes first, and generated settings use their own order.
    /// </summary>
    public int Order { get; init; }
}

/// <summary>
/// A <see cref="CustomSettingsSection"/> in the settings window.
/// </summary>
public sealed class CustomSettingsSectionViewModel : SettingsEntryViewModel
{
    private readonly Lazy<ViewModelBase> content;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomSettingsSectionViewModel"/> class.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="localizer">UI strings.</param>
    public CustomSettingsSectionViewModel(CustomSettingsSection section, Localizer localizer)
        : base(section.Category, section.LabelKey, localizer)
    {
        content = new Lazy<ViewModelBase>(section.CreateViewModel);
    }

    /// <summary>
    /// Gets the section's own view model.
    /// </summary>
    public ViewModelBase Content => content.Value;
}
