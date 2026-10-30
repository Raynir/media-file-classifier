using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace MediaFileClassifier.Ui.Tests.Theming;

/// <summary>
/// Checks the design tokens (E13-S06-T02) and that views use them instead of literal values.
/// </summary>
public sealed class TokenTests
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string XamlDirectory = Path.Combine(AppContext.BaseDirectory, "UiViews");
    private static readonly string[] Statuses = ["Pending", "Error", "Missing", "Offline", "Excluded"];
    private static readonly string[] Provenances = ["Ai", "Edited", "Accepted", "NewSuggestion"];

    /// <summary>
    /// Attributes whose values must come from tokens or bindings in views.
    /// </summary>
    private static readonly HashSet<string> TokenOnlyAttributes =
    [
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color",
        "FontSize", "FontWeight", "FontFamily", "Margin", "Padding", "Spacing", "CornerRadius",
    ];

    public static TheoryData<string> ViewFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(XamlDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(XamlDirectory, file);
            if (!relative.StartsWith("Theming", StringComparison.Ordinal))
            {
                data.Add(relative);
            }
        }

        return data;
    }

    [Fact]
    public void LightAndDark_DefineTheSameColourTokens()
    {
        var (light, dark) = ThemeKeys();

        Assert.NotEmpty(dark);
        Assert.Equal(dark.Order(), light.Order());
    }

    [Fact]
    public void Tokens_IncludeEveryItemStatusAndProvenanceSlot()
    {
        var (_, dark) = ThemeKeys();
        var icons = KeysIn("Theming/Icons.axaml");

        foreach (var status in Statuses)
        {
            Assert.Contains($"Brush.Status.{status}.Foreground", dark);
            Assert.Contains($"Brush.Status.{status}.Background", dark);
            Assert.Contains($"Icon.Status.{status}", icons);
        }

        foreach (var provenance in Provenances)
        {
            Assert.Contains($"Brush.Provenance.{provenance}.Foreground", dark);
            Assert.Contains($"Brush.Provenance.{provenance}.Background", dark);
            Assert.Contains($"Icon.Provenance.{provenance}", icons);
        }
    }

    [Fact]
    public void Tokens_IncludeSpacingRadiiAndTypography()
    {
        var keys = KeysIn("Theming/Tokens.axaml");

        Assert.Contains("Space.S", keys);
        Assert.Contains("Radius.Control", keys);
        Assert.Contains("FontSize.Body", keys);
        Assert.Contains("FontWeight.Title", keys);
        Assert.Contains("Opacity.Disabled", keys);
    }

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void View_TakesColoursAndSizesFromTokens(string file)
    {
        var document = XDocument.Load(Path.Combine(XamlDirectory, file));

        var literals = document.Descendants()
            .SelectMany(element => element.Attributes().Select(attribute => (element, attribute)))
            .Where(pair => TokenOnlyAttributes.Contains(pair.attribute.Name.LocalName)
                && !pair.attribute.Value.TrimStart().StartsWith('{')
                && pair.attribute.Value != "Transparent")
            .Select(pair => $"<{pair.element.Name.LocalName} {pair.attribute.Name.LocalName}=\"{pair.attribute.Value}\">")
            .ToList();

        Assert.True(
            literals.Count == 0,
            $"{file} has literal colours or sizes. Use a token from Theming/Tokens.axaml:\n" + string.Join('\n', literals));
    }

    [Fact]
    public Task Tokens_ResolveDifferentlyInLightAndDark() => TestApp.RunAsync(() =>
    {
        var application = Application.Current!;

        Assert.True(application.TryGetResource("Brush.Surface.Window", ThemeVariant.Light, out var light));
        Assert.True(application.TryGetResource("Brush.Surface.Window", ThemeVariant.Dark, out var dark));
        Assert.NotEqual(((ISolidColorBrush)light!).Color, ((ISolidColorBrush)dark!).Color);
    });

    private static (HashSet<string> Light, HashSet<string> Dark) ThemeKeys()
    {
        var document = XDocument.Load(Path.Combine(XamlDirectory, "Theming", "Tokens.axaml"));
        HashSet<string> KeysOf(string theme) => document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == theme)
            .Elements()
            .Select(element => (string)element.Attribute(XName.Get("Key", XamlNamespace))!)
            .ToHashSet();

        return (KeysOf("Light"), KeysOf("Dark"));
    }

    private static HashSet<string> KeysIn(string file)
        => XDocument.Load(Path.Combine(XamlDirectory, file)).Descendants()
            .Select(element => (string?)element.Attribute(XName.Get("Key", XamlNamespace)))
            .OfType<string>()
            .ToHashSet();
}
