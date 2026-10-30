using System.Globalization;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Tests for <see cref="PseudoLocalizer"/> and the pseudo-localisation mode (E13-S08-T03).
/// </summary>
public sealed class PseudoLocalizerTests
{
    [Fact]
    public void Transform_AccentsBracketsAndLengthens()
    {
        Assert.Equal("[Óƥéñ ·]", PseudoLocalizer.Transform("Open"));
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("No library open")]
    [InlineData("Create a library or open an existing one to start indexing your media.")]
    public void Transform_LengthensByAtLeast30Percent(string text)
    {
        Assert.True(PseudoLocalizer.Transform(text).Length >= text.Length * 1.3);
    }

    [Fact]
    public void Transform_KeepsPlaceholdersAndAccessKeys()
    {
        var result = PseudoLocalizer.Transform("_Open {0} of {1:N0}");

        Assert.StartsWith("[_Óƥéñ {0} óƒ {1:N0}", result, StringComparison.Ordinal);
        Assert.Equal("[_Óƥéñ 5 óƒ 1,000", string.Format(CultureInfo.GetCultureInfo("en-US"), result, 5, 1000)[..17]);
    }

    [Fact]
    public void Localizer_PseudoLocalizesWhenSwitchedOn()
    {
        var localizer = new Localizer(
            new System.Resources.ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
        };
        var changes = 0;
        localizer.Changed += (_, _) => changes++;

        localizer.IsPseudoLocalized = true;

        Assert.Equal(1, changes);
        Assert.Equal(PseudoLocalizer.Transform("Media File Classifier"), localizer.Get("App_Title"));
    }
}
