using System.Globalization;
using System.Resources;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// Creates localizers that tests can switch without affecting <see cref="Localizer.Current"/>,
/// which other tests read in parallel.
/// </summary>
internal static class TestLocalizer
{
    /// <summary>
    /// Creates a localizer for en-US over the app's own resources.
    /// </summary>
    /// <returns>A new localizer.</returns>
    public static Localizer EnUs() => new(
        new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
    };
}
