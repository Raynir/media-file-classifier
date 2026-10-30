using System.Text;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Turns English text into pseudo-localised text (E13-S08-T03): accented letters show strings that
/// skipped the resource files, the text is about 40% longer, like many translations, and brackets show
/// where it's cut off. For example, <c>"Open"</c> becomes <c>"[Óƥéñ ·]"</c>.
/// </summary>
public static class PseudoLocalizer
{
    private const double Expansion = 0.4;
    private const string Padding = " ·········";

    private static readonly Dictionary<char, char> Accents = new()
    {
        ['a'] = 'á', ['b'] = 'ƀ', ['c'] = 'ç', ['d'] = 'ð', ['e'] = 'é', ['f'] = 'ƒ', ['g'] = 'ĝ',
        ['h'] = 'ĥ', ['i'] = 'í', ['j'] = 'ĵ', ['k'] = 'ķ', ['l'] = 'ļ', ['m'] = 'ɱ', ['n'] = 'ñ',
        ['o'] = 'ó', ['p'] = 'ƥ', ['q'] = 'ʠ', ['r'] = 'ŕ', ['s'] = 'š', ['t'] = 'ţ', ['u'] = 'ú',
        ['v'] = 'ṽ', ['w'] = 'ŵ', ['x'] = 'ẋ', ['y'] = 'ý', ['z'] = 'ž',
        ['A'] = 'Å', ['B'] = 'Ɓ', ['C'] = 'Ç', ['D'] = 'Ð', ['E'] = 'É', ['F'] = 'Ƒ', ['G'] = 'Ĝ',
        ['H'] = 'Ĥ', ['I'] = 'Í', ['J'] = 'Ĵ', ['K'] = 'Ķ', ['L'] = 'Ļ', ['M'] = 'Ṁ', ['N'] = 'Ñ',
        ['O'] = 'Ó', ['P'] = 'Ƥ', ['Q'] = 'Ǫ', ['R'] = 'Ŕ', ['S'] = 'Š', ['T'] = 'Ţ', ['U'] = 'Ú',
        ['V'] = 'Ṽ', ['W'] = 'Ŵ', ['X'] = 'Ẋ', ['Y'] = 'Ý', ['Z'] = 'Ž',
    };

    /// <summary>
    /// Pseudo-localises a string. Format placeholders such as <c>{0}</c> and the access-key marker
    /// <c>_</c> are kept, so formatting and menu access keys still work.
    /// </summary>
    /// <param name="text">English text.</param>
    /// <returns>The pseudo-localised text.</returns>
    public static string Transform(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length * 2);
        builder.Append('[');
        var placeholderDepth = 0;
        foreach (var c in text)
        {
            placeholderDepth += c switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
            builder.Append(placeholderDepth == 0 && Accents.TryGetValue(c, out var accented) ? accented : c);
        }

        var padding = Math.Max(2, (int)Math.Ceiling(text.Length * Expansion));
        for (var i = 0; i < padding; i++)
        {
            builder.Append(Padding[i % Padding.Length]);
        }

        builder.Append(']');
        return builder.ToString();
    }
}
