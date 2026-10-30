using System.Globalization;
using System.Resources;
using System.Xml.Linq;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Checks that the views take every user-facing string from the resource files (E13-S08-T01).
/// The views are copied into the test output by the project file, so new views are checked automatically.
/// </summary>
public sealed class HardCodedStringTests
{
    private static readonly string ViewsDirectory = Path.Combine(AppContext.BaseDirectory, "UiViews");

    public static TheoryData<string> ViewFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ViewsDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(ViewsDirectory, file));
        }

        return data;
    }

    [Fact]
    public void ViewsAreCopiedForTheCheck()
    {
        Assert.NotEmpty(ViewFiles());
    }

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void View_HasNoHardCodedStrings(string file)
    {
        var document = XDocument.Load(Path.Combine(ViewsDirectory, file));

        var violations = HardCodedStringScanner.Scan(document).ToList();

        Assert.True(
            violations.Count == 0,
            $"{file} has hard-coded UI strings. Move them to Resources/Strings.resx and bind them with {{l:Loc Key}}:\n"
                + string.Join('\n', violations));
    }

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void View_UsesOnlyExistingResourceKeys(string file)
    {
        var keys = ResourceKeys();

        var missing = HardCodedStringScanner.LocKeys(File.ReadAllText(Path.Combine(ViewsDirectory, file)))
            .Where(key => !keys.Contains(key))
            .ToList();

        Assert.True(missing.Count == 0, $"{file} uses keys missing from Strings.resx: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui" Text="Hello" />""")]
    [InlineData("""<Button xmlns="https://github.com/avaloniaui" Content="Open" />""")]
    [InlineData("""<Button xmlns="https://github.com/avaloniaui" ToolTip.Tip="Opens a library" />""")]
    [InlineData("""<Button xmlns="https://github.com/avaloniaui" AutomationProperties.Name="Close" />""")]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui">Hello</TextBlock>""")]
    public void Scan_FlagsHardCodedText(string xaml)
    {
        Assert.NotEmpty(HardCodedStringScanner.Scan(XDocument.Parse(xaml)));
    }

    [Theory]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui" Text="{l:Loc Some_Key}" />""")]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui" Text="{Binding Name}" />""")]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui" Text=" · " />""")]
    [InlineData("""<TextBlock xmlns="https://github.com/avaloniaui" Name="Title" Classes="Heading" />""")]
    public void Scan_AllowsBoundAndNonTextValues(string xaml)
    {
        Assert.Empty(HardCodedStringScanner.Scan(XDocument.Parse(xaml)));
    }

    private static HashSet<string> ResourceKeys()
    {
        var resources = new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Ui.Localization.Localizer).Assembly);
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        return set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet();
    }
}
