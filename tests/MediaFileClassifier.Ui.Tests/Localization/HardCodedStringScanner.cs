using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Finds user-facing text written straight into XAML instead of coming from the resource files (E13-S08-T01).
/// </summary>
public static partial class HardCodedStringScanner
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Attributes that show text to the user, matched by local name, so attached properties such as
    /// <c>ToolTip.Tip</c> are covered too.
    /// </summary>
    private static readonly HashSet<string> TextAttributes =
    [
        "Content",
        "Description",
        "Header",
        "InputGesture",
        "PlaceholderText",
        "Text",
        "Title",
        "Watermark",
        "ToolTip.Tip",
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
    ];

    /// <summary>
    /// Lists hard-coded user-facing strings in a XAML document: literal values of text attributes,
    /// and text inside elements. Values without letters, such as separators, are allowed.
    /// </summary>
    /// <param name="document">The XAML document.</param>
    /// <returns>A description of each violation.</returns>
    public static IEnumerable<string> Scan(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (TextAttributes.Contains(attribute.Name.LocalName) && IsHardCoded(attribute.Value))
                {
                    yield return $"<{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">";
                }
            }

            // XAML-language elements such as x:Double, and keyed resources such as icon geometry, aren't displayed text.
            if (element.Name.NamespaceName == XamlNamespace || element.Attribute(XName.Get("Key", XamlNamespace)) is not null)
            {
                continue;
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                if (IsHardCoded(text.Value))
                {
                    yield return $"<{element.Name.LocalName}>{text.Value.Trim()}</{element.Name.LocalName}>";
                }
            }
        }
    }

    /// <summary>
    /// Lists the resource keys used through <c>{l:Loc Key}</c> in a XAML file's text.
    /// </summary>
    /// <param name="xaml">The XAML text.</param>
    /// <returns>The keys.</returns>
    public static IEnumerable<string> LocKeys(string xaml)
        => LocPattern().Matches(xaml).Select(match => match.Groups["key"].Value);

    private static bool IsHardCoded(string value)
    {
        var trimmed = value.Trim();
        return !trimmed.StartsWith('{') && trimmed.Any(char.IsLetter);
    }

    [GeneratedRegex(@"\{\w+:Loc\s+(?:Key=)?(?<key>[\w.]+)\s*\}")]
    private static partial Regex LocPattern();
}
